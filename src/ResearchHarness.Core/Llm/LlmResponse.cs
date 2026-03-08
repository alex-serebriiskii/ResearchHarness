namespace ResearchHarness.Core.Llm;

/// <summary>
/// Response from the LLM client. The generic variant carries a deserialized T.
/// TokenUsage is always present for cost tracking.
/// </summary>
public record LlmResponse<T>(
    T Content,
    TokenUsage Usage,
    string StopReason
);

public record TokenUsage(
    int InputTokens,
    int OutputTokens
)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
