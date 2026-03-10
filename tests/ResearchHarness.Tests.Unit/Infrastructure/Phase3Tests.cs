using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;
using ResearchHarness.Infrastructure.Llm;
using ResearchHarness.Infrastructure.Search;
using ResearchHarness.Infrastructure.Telemetry;
using ResearchHarness.Infrastructure.Tracking;

namespace ResearchHarness.Tests.Unit.Infrastructure;

internal record AnthropicTestPayload(int Value, string SubTopic);

public sealed class Phase3Tests
{
    // ── TokenTracker ──────────────────────────────────────────────────────────

    [Test]
    public void TokenTracker_Record_AccumulatesTokens()
    {
        var tracker = new TokenTracker();
        tracker.Record("gpt-4o", 100, 50);
        tracker.Record("gpt-4o", 200, 100);
        tracker.Record("claude-3", 300, 150);

        var summary = tracker.GetSummary();
        summary.TotalInputTokens.Should().Be(600);
        summary.TotalOutputTokens.Should().Be(300);
        summary.TotalLlmCalls.Should().Be(3);
        summary.ByModel.Should().HaveCount(2);
        summary.ByModel["gpt-4o"].Calls.Should().Be(2);
        summary.ByModel["claude-3"].InputTokens.Should().Be(300);
    }

    [Test]
    public void TokenTracker_GetSummary_EmptyTracker_ReturnsZeroes()
    {
        var tracker = new TokenTracker();

        var summary = tracker.GetSummary();

        summary.TotalInputTokens.Should().Be(0);
        summary.TotalLlmCalls.Should().Be(0);
        summary.ByModel.Should().BeEmpty();
    }

    // ── TrackingLlmClient ─────────────────────────────────────────────────────

    [Test]
    public async Task TrackingLlmClient_CompleteAsync_RecordsTokenUsage()
    {
        var tracker = new TokenTracker();
        var inner = Substitute.For<ILlmClient>();

        var meterFactory = Substitute.For<IMeterFactory>();
        var meter = new Meter("test");
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        var metrics = new ResearchMetrics(meterFactory);

        var fakeResponse = new LlmResponse<string>("hello", new TokenUsage(10, 20), "stop");
        inner.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
             .Returns(fakeResponse);

        var client = new TrackingLlmClient(inner, tracker, metrics);
        var request = new LlmRequest("gpt-4o", "sys", [LlmMessage.User("hello")]);

        var result = await client.CompleteAsync(request);

        result.Content.Should().Be("hello");
        var summary = tracker.GetSummary();
        summary.TotalInputTokens.Should().Be(10);
        summary.TotalOutputTokens.Should().Be(20);
        summary.TotalLlmCalls.Should().Be(1);
    }

    // ── SearchResultCache ─────────────────────────────────────────────────────

    [Test]
    public async Task SearchResultCache_GetAsync_AfterSetAsync_ReturnsValue()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new BraveSearchOptions { CacheTtl = TimeSpan.FromMinutes(5) });
        var cache = new SearchResultCache(memoryCache, options);

        var results = new SearchResults(
            [new SearchHit("https://example.com", "Example", "desc", null)],
            null);
        await cache.SetAsync("test query", results);

        var cached = await cache.GetAsync("test query");
        cached.Should().NotBeNull();
        cached!.Hits.Should().HaveCount(1);
        cached.Hits[0].Url.Should().Be("https://example.com");
    }

    [Test]
    public async Task SearchResultCache_GetAsync_UnknownQuery_ReturnsNull()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new BraveSearchOptions { CacheTtl = TimeSpan.FromMinutes(5) });
        var cache = new SearchResultCache(memoryCache, options);

        var result = await cache.GetAsync("unknown query");

        result.Should().BeNull();
    }

    // ── AnthropicLlmClient — snake_case deserialization ───────────────────────

    [Test]
    public async Task AnthropicLlmClient_ToolUse_DeserializesSnakeCaseFields()
    {
        // sub_topic (snake_case) must map to SubTopic (PascalCase) — the BF-1 fix
        var inputJson = """{"value": 42, "sub_topic": "test"}""";
        var responseJson = AnthropicToolUseResponseJson(inputJson);

        var handler = new FakeHttpHandler(OkJson(responseJson));
        var client = BuildAnthropicClient(handler);

        var schema = JsonNode.Parse("""{"type":"object"}""")!.AsObject();
        var request = new LlmRequest(
            "claude-3-opus-20240229",
            "sys",
            [LlmMessage.User("hello")],
            OutputSchema: schema);

        var result = await client.CompleteAsync<AnthropicTestPayload>(request);

        result.Content.Value.Should().Be(42);
        result.Content.SubTopic.Should().Be("test");
        result.Usage.InputTokens.Should().Be(10);
        result.Usage.OutputTokens.Should().Be(5);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string AnthropicToolUseResponseJson(string inputJson) => $$"""
        {
            "content": [{"type": "tool_use", "input": {{inputJson}}}],
            "stop_reason": "tool_use",
            "usage": {"input_tokens": 10, "output_tokens": 5}
        }
        """;

    private static AnthropicLlmClient BuildAnthropicClient(FakeHttpHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Anthropic").Returns(httpClient);

        var options = Options.Create(new AnthropicOptions
        {
            ApiKey = "test",
            BaseUrl = "https://api.anthropic.com",
            Version = "2023-06-01",
            MaxRetries = 0,
            MaxConcurrentLlmCalls = 1
        });

        var rateLimiter = new RateLimitedExecutor(maxLlmConcurrency: 1, maxSearchConcurrency: 1);
        var logger = Substitute.For<ILogger<AnthropicLlmClient>>();

        return new AnthropicLlmClient(factory, rateLimiter, options, logger);
    }

    private static HttpResponseMessage OkJson(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public FakeHttpHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                await request.Content.ReadAsStringAsync(cancellationToken);

            return _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        }
    }
}
