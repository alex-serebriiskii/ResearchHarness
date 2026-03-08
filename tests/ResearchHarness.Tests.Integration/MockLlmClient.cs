using System.Collections.Concurrent;
using System.Text.Json;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Tests.Integration;

/// <summary>
/// Deterministic LLM client for integration tests.
/// Responses are dequeued in order; after the queue is empty, returns the <see cref="DefaultResponse"/>.
/// </summary>
internal sealed class MockLlmClient : ILlmClient
{
    // Same settings as AgentSerializerOptions.Default (that class is internal to Agents).
    private static readonly JsonSerializerOptions AgentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentQueue<string> _queue = new();

    /// <summary>Response returned when the queue is empty.</summary>
    public string DefaultResponse { get; set; } = "{}";

    public void Enqueue(string responseContent) => _queue.Enqueue(responseContent);

    public Task<LlmResponse<T>> CompleteAsync<T>(LlmRequest request, CancellationToken ct = default)
    {
        var json = _queue.TryDequeue(out var next) ? next : DefaultResponse;
        var content = JsonSerializer.Deserialize<T>(json, AgentOptions)!;
        return Task.FromResult(new LlmResponse<T>(content, new TokenUsage(0, 0), "stop"));
    }

    public Task<LlmResponse<string>> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var content = _queue.TryDequeue(out var next) ? next : DefaultResponse;
        return Task.FromResult(new LlmResponse<string>(content, new TokenUsage(0, 0), "stop"));
    }
}
