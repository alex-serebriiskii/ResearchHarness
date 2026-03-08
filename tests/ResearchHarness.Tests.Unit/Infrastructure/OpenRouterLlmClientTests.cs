using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ResearchHarness.Core.Llm;
using ResearchHarness.Infrastructure.Llm;

namespace ResearchHarness.Tests.Unit.Infrastructure;

// Placed at namespace level so System.Text.Json reflection can access the constructor.
internal record OpenRouterTestPayload(int Value);

/// <summary>
/// Unit tests for OpenRouterLlmClient. HTTP transport is intercepted via a
/// FakeHttpMessageHandler so no real network calls are made.
/// </summary>
public class OpenRouterLlmClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// HttpMessageHandler that returns a canned sequence of responses.
    /// Each SendAsync call dequeues the next response; the last is reused when
    /// the queue is exhausted. Request bodies are captured before HttpClient
    /// disposes the request message.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public FakeHttpHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Read the body NOW — HttpClient will dispose it after SendAsync returns.
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : "";
            RequestBodies.Add(body);

            var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return response;
        }
    }

    private static HttpResponseMessage OkJson(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode code) =>
        new(code) { Content = new StringContent("{\"error\":\"fail\"}", Encoding.UTF8, "application/json") };

    /// <summary>
    /// Builds a minimal valid OpenAI-compatible text completion response.
    /// </summary>
    private static string TextCompletionJson(string text, string finishReason = "stop") => $$"""
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": {{JsonSerializer.Serialize(text)}}
              },
              "finish_reason": {{JsonSerializer.Serialize(finishReason)}}
            }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 20 }
        }
        """;

    /// <summary>
    /// Builds a minimal valid OpenAI-compatible tool_calls response for
    /// structured output via function calling. The key "tool_calls" matches
    /// the [JsonPropertyName("tool_calls")] attribute on the DTO.
    /// </summary>
    private static string ToolCallJson(string argumentsJson, string finishReason = "tool_calls") => $$"""
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  {
                    "type": "function",
                    "function": {
                      "name": "respond",
                      "arguments": {{JsonSerializer.Serialize(argumentsJson)}}
                    }
                  }
                ]
              },
              "finish_reason": {{JsonSerializer.Serialize(finishReason)}}
            }
          ],
          "usage": { "prompt_tokens": 15, "completion_tokens": 30 }
        }
        """;

    private static OpenRouterLlmClient BuildClient(FakeHttpHandler handler, int maxRetries = 0)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("OpenRouter").Returns(httpClient);

        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://openrouter.ai/api",
            MaxConcurrentLlmCalls = 1,
            MaxRetries = maxRetries
        });

        var rateLimiter = new RateLimitedExecutor(maxLlmConcurrency: 1, maxSearchConcurrency: 1);
        var logger = Substitute.For<ILogger<OpenRouterLlmClient>>();

        return new OpenRouterLlmClient(factory, rateLimiter, options, logger);
    }

    private static LlmRequest TextRequest(string model = "openai/gpt-4o-mini") =>
        new(model, "You are a helpful assistant.", [LlmMessage.User("Hello")]);

    private static LlmRequest StructuredRequest(JsonObject schema, string model = "openai/gpt-4o-mini") =>
        new(model, "You are a helpful assistant.", [LlmMessage.User("Hello")], OutputSchema: schema);

    // ── Text completion ────────────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_TextRequest_ReturnsMessageContent()
    {
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("Hello back!")));
        var client = BuildClient(handler);

        var result = await client.CompleteAsync(TextRequest());

        result.Content.Should().Be("Hello back!");
        result.Usage.InputTokens.Should().Be(10);
        result.Usage.OutputTokens.Should().Be(20);
        result.StopReason.Should().Be("stop");
    }

    [Test]
    public async Task CompleteAsync_Generic_StringType_ReturnsTextWithoutJsonRoundTrip()
    {
        // The fast path for T=string must not JSON-parse the content.
        var raw = "This is plain text, not JSON.";
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson(raw)));
        var client = BuildClient(handler);

        var result = await client.CompleteAsync<string>(TextRequest());

        result.Content.Should().Be(raw);
    }

    [Test]
    public async Task CompleteAsync_Generic_DeserializesJsonContent()
    {
        var json = """{"value":42}""";
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson(json)));
        var client = BuildClient(handler);

        var result = await client.CompleteAsync<OpenRouterTestPayload>(TextRequest());

        result.Content.Value.Should().Be(42);
    }

    // ── Structured output (tool calling) ──────────────────────────────────────

    [Test]
    public async Task CompleteAsync_StructuredRequest_DeserializesToolCallArguments()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"value":{"type":"integer"}}}""")!.AsObject();
        var argumentsJson = """{"value":99}""";
        var handler = new FakeHttpHandler(OkJson(ToolCallJson(argumentsJson)));
        var client = BuildClient(handler);

        var result = await client.CompleteAsync<OpenRouterTestPayload>(StructuredRequest(schema));

        result.Content.Value.Should().Be(99);
        result.StopReason.Should().Be("tool_calls");
    }

    [Test]
    public async Task CompleteAsync_StructuredRequest_MissingToolCalls_ThrowsLlmException()
    {
        // API responds with text only — no tool_calls block despite schema being set.
        var schema = JsonNode.Parse("""{"type":"object"}""")!.AsObject();
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("oops, no tool call")));
        var client = BuildClient(handler);

        Func<Task> act = () => client.CompleteAsync<OpenRouterTestPayload>(StructuredRequest(schema));

        await act.Should().ThrowAsync<LlmException>()
            .WithMessage("*tool_calls*");
    }

    // ── Retry logic ───────────────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_On429_RetriesAndSucceeds()
    {
        // First call returns 429, second call succeeds.
        var handler = new FakeHttpHandler(
            ErrorResponse(HttpStatusCode.TooManyRequests),
            OkJson(TextCompletionJson("retry success")));
        var client = BuildClient(handler, maxRetries: 1);

        var result = await client.CompleteAsync(TextRequest());

        result.Content.Should().Be("retry success");
        handler.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task CompleteAsync_OnServerError_RetriesAndSucceeds()
    {
        var handler = new FakeHttpHandler(
            ErrorResponse(HttpStatusCode.InternalServerError),
            OkJson(TextCompletionJson("recovered")));
        var client = BuildClient(handler, maxRetries: 1);

        var result = await client.CompleteAsync(TextRequest());

        result.Content.Should().Be("recovered");
        handler.Requests.Should().HaveCount(2);
    }

    [Test]
    public async Task CompleteAsync_ExhaustsRetries_ThrowsLlmException()
    {
        var handler = new FakeHttpHandler(ErrorResponse(HttpStatusCode.TooManyRequests));
        var client = BuildClient(handler, maxRetries: 0); // no retries

        Func<Task> act = () => client.CompleteAsync(TextRequest());

        await act.Should().ThrowAsync<LlmException>()
            .WithMessage("*HTTP 429*");
    }

    [Test]
    public async Task CompleteAsync_On400_DoesNotRetry()
    {
        // 400 Bad Request is not retryable. Must not retry even when MaxRetries > 0.
        var handler = new FakeHttpHandler(ErrorResponse(HttpStatusCode.BadRequest));
        var client = BuildClient(handler, maxRetries: 3);

        Func<Task> act = () => client.CompleteAsync(TextRequest());

        await act.Should().ThrowAsync<LlmException>();
        handler.Requests.Should().HaveCount(1);
    }

    // ── Token usage ───────────────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_MapsPromptAndCompletionTokens()
    {
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("hi")));
        var client = BuildClient(handler);

        var result = await client.CompleteAsync(TextRequest());

        result.Usage.InputTokens.Should().Be(10);
        result.Usage.OutputTokens.Should().Be(20);
        result.Usage.TotalTokens.Should().Be(30);
    }

    // ── Request shape ─────────────────────────────────────────────────────────

    [Test]
    public async Task CompleteAsync_SetsAuthorizationHeader()
    {
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("ok")));
        var client = BuildClient(handler);

        await client.CompleteAsync(TextRequest());

        var request = handler.Requests.Single();
        request.Headers.TryGetValues("Authorization", out var values).Should().BeTrue();
        values!.Single().Should().Be("Bearer test-key");
    }

    [Test]
    public async Task CompleteAsync_PostsToCorrectEndpoint()
    {
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("ok")));
        var client = BuildClient(handler);

        await client.CompleteAsync(TextRequest());

        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.Should().EndWith("/v1/chat/completions");
    }

    [Test]
    public async Task CompleteAsync_IncludesSystemMessageFirst()
    {
        var handler = new FakeHttpHandler(OkJson(TextCompletionJson("ok")));
        var client = BuildClient(handler);

        await client.CompleteAsync(new LlmRequest(
            "openai/gpt-4o-mini",
            "SysPrompt",
            [LlmMessage.User("UserMsg")]));

        // Use the pre-captured body string — the request is disposed by HttpClient after SendAsync.
        var body = JsonDocument.Parse(handler.RequestBodies.Single());
        var messages = body.RootElement.GetProperty("messages");

        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("SysPrompt");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("UserMsg");
    }

    [Test]
    public async Task CompleteAsync_NoChoices_ThrowsLlmException()
    {
        var emptyChoices = """{"choices":[],"usage":{"prompt_tokens":0,"completion_tokens":0}}""";
        var handler = new FakeHttpHandler(OkJson(emptyChoices));
        var client = BuildClient(handler);

        Func<Task> act = () => client.CompleteAsync(TextRequest());

        await act.Should().ThrowAsync<LlmException>()
            .WithMessage("*no choices*");
    }
}
