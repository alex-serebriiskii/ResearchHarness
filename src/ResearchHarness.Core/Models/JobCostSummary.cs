namespace ResearchHarness.Core.Models;

public record JobCostSummary(
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalLlmCalls,
    Dictionary<string, ModelUsage> ByModel);

public record ModelUsage(string Model, int InputTokens, int OutputTokens, int Calls);
