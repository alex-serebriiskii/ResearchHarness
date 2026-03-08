using ResearchHarness.Core.Llm;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Unified interface for LLM API calls. Handles model routing, retry/backoff,
/// token tracking, and rate limiting. The generic overload deserializes the
/// response content as T using System.Text.Json.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse<T>> CompleteAsync<T>(LlmRequest request, CancellationToken ct = default);
    Task<LlmResponse<string>> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
