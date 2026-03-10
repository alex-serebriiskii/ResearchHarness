using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Infrastructure.Telemetry;

namespace ResearchHarness.Infrastructure.Tracking;

/// <summary>
/// Decorator around ILlmClient that records token usage to ITokenTracker.
/// Registered as scoped so each job scope gets its own accumulator.
/// </summary>
public sealed class TrackingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly ITokenTracker _tracker;
    private readonly ResearchMetrics _metrics;

    public TrackingLlmClient(ILlmClient inner, ITokenTracker tracker, ResearchMetrics metrics)
    {
        _inner = inner;
        _tracker = tracker;
        _metrics = metrics;
    }

    public async Task<LlmResponse<T>> CompleteAsync<T>(LlmRequest request, CancellationToken ct = default)
    {
        var response = await _inner.CompleteAsync<T>(request, ct);
        _tracker.Record(request.Model, response.Usage.InputTokens, response.Usage.OutputTokens);
        return response;
    }

    public async Task<LlmResponse<string>> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var response = await _inner.CompleteAsync(request, ct);
        _tracker.Record(request.Model, response.Usage.InputTokens, response.Usage.OutputTokens);
        return response;
    }
}
