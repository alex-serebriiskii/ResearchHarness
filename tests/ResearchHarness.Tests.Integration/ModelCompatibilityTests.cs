using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Llm;
using ResearchHarness.Infrastructure.Llm;

namespace ResearchHarness.Tests.Integration;

/// <summary>
/// Model compatibility harness: sends a minimal tool-use (function calling)
/// request to each candidate model via OpenRouter and verifies the response
/// contains a valid tool_calls block that deserializes to the expected type.
///
/// This catches models that silently ignore function calling, return malformed
/// tool output, or produce JSON that fails deserialization — before you commit
/// to using them in the research pipeline.
///
/// Gated behind two environment variables:
///   RUN_MODEL_COMPAT_TESTS=true   — opt-in flag
///   OPENROUTER_API_KEY=sk-or-...  — real API key
///
/// Not run on CI without real credentials.
/// </summary>
public class ModelCompatibilityTests
{
    private static bool ShouldRun =>
        Environment.GetEnvironmentVariable("RUN_MODEL_COMPAT_TESTS") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));

    /// <summary>
    /// Candidate models to probe. Update this list before switching models
    /// in appsettings. Each entry is an OpenRouter model identifier.
    /// </summary>
    public static IEnumerable<string> CandidateModels()
    {
        yield return "meta-llama/llama-3.3-70b-instruct";
        yield return "minimax/minimax-m2.5";
        yield return "google/gemini-flash-1.5";
        yield return "mistralai/mistral-nemo";
        yield return "google/gemma-2-9b-it";
        yield return "meta-llama/llama-3.1-8b-instruct";
    }

    /// <summary>
    /// Minimal structured output type. Small enough that any model can handle it,
    /// but structured enough to prove tool_calls deserialization works end-to-end.
    /// </summary>
    private record ProbeResponse(int Answer, double Confidence, string Reasoning);

    /// <summary>
    /// The JSON schema sent as the tool's parameters definition.
    /// Mirrors <see cref="ProbeResponse"/> in snake_case for model consumption.
    /// </summary>
    private static readonly JsonObject ProbeSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["answer"] = new JsonObject { ["type"] = "integer", ["description"] = "The numeric answer" },
            ["confidence"] = new JsonObject { ["type"] = "number", ["description"] = "Confidence between 0 and 1" },
            ["reasoning"] = new JsonObject { ["type"] = "string", ["description"] = "Brief explanation" }
        },
        ["required"] = new JsonArray("answer", "confidence", "reasoning")
    };

    private static (OpenRouterLlmClient Client, RateLimitedExecutor RateLimiter) CreateClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!;

        var options = Options.Create(new OpenRouterOptions
        {
            ApiKey = apiKey,
            BaseUrl = "https://openrouter.ai/api",
            MaxRetries = 2,
            RateLimitRetryBaseDelaySeconds = 10.0,
            MaxConcurrentLlmCalls = 1
        });

        // Real HTTP -- no mocking
        var factory = new SimpleHttpClientFactory();
        var rateLimiter = new RateLimitedExecutor(maxLlmConcurrency: 1);
        var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .CreateLogger<OpenRouterLlmClient>();

        var client = new OpenRouterLlmClient(factory, rateLimiter, options, logger);
        return (client, rateLimiter);
    }

    private static LlmRequest BuildProbeRequest(string model) =>
        new(
            Model: model,
            SystemPrompt: "You are a calculator. Answer the math question using the provided tool.",
            Messages: [LlmMessage.User("What is 17 + 25?")],
            OutputSchema: ProbeSchema,
            MaxTokens: 256,
            Temperature: 0.0
        );

    // ── Tests ────────────────────────────────────────────────────────────────

    [Test]
    [MethodDataSource(nameof(CandidateModels))]
    public async Task ToolCallProbe_ReturnsValidStructuredOutput(string model)
    {
        if (!ShouldRun)
        {
            // Env vars not set — skip without failing
            await Task.CompletedTask;
            return;
        }

        var (client, rateLimiter) = CreateClient();
        using var _ = rateLimiter;
        var request = BuildProbeRequest(model);

        var response = await client.CompleteAsync<ProbeResponse>(request, CancellationToken.None);

        // The model returned a parseable tool_calls block
        response.Content.Should().NotBeNull();

        // The answer field is populated (42 is correct, but we're testing structure, not arithmetic)
        response.Content.Answer.Should().BeGreaterThan(0);

        // Confidence is in a reasonable range
        response.Content.Confidence.Should().BeGreaterThanOrEqualTo(0);
        response.Content.Confidence.Should().BeLessThanOrEqualTo(1.0);

        // Reasoning is non-empty
        response.Content.Reasoning.Should().NotBeNullOrWhiteSpace();

        // Token usage proves the API actually processed the request
        response.Usage.TotalTokens.Should().BeGreaterThan(0);
        response.Usage.InputTokens.Should().BeGreaterThan(0);
        response.Usage.OutputTokens.Should().BeGreaterThan(0);

        // Stop reason is present
        response.StopReason.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void HarnessConfiguration_IsValid()
    {
        // Verifies the harness compiles, the candidate list is non-empty,
        // and the probe schema is well-formed — runs without API keys.
        CandidateModels().Should().NotBeEmpty();
        ProbeSchema["type"]!.GetValue<string>().Should().Be("object");
        ProbeSchema["properties"]!.AsObject().Count.Should().Be(3);
        ProbeSchema["required"]!.AsArray().Count.Should().Be(3);
    }

    // ── Minimal IHttpClientFactory for test use ──────────────────────────────

    /// <summary>
    /// Bare IHttpClientFactory that returns a default HttpClient.
    /// Avoids pulling in the full DI/hosting stack for a focused integration test.
    /// </summary>
    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
