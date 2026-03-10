using System.Collections.Concurrent;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Tracking;

public sealed class TokenTracker : ITokenTracker
{
    private record struct Accum(int InputTokens, int OutputTokens, int Calls);

    private readonly ConcurrentDictionary<string, Accum> _byModel = new();

    public void Record(string model, int inputTokens, int outputTokens)
    {
        _byModel.AddOrUpdate(
            model,
            new Accum(inputTokens, outputTokens, 1),
            (_, existing) => new Accum(
                existing.InputTokens + inputTokens,
                existing.OutputTokens + outputTokens,
                existing.Calls + 1));
    }

    public JobCostSummary GetSummary()
    {
        var byModel = _byModel.ToDictionary(
            kvp => kvp.Key,
            kvp => new ModelUsage(kvp.Key, kvp.Value.InputTokens, kvp.Value.OutputTokens, kvp.Value.Calls));
        return new JobCostSummary(
            TotalInputTokens: byModel.Values.Sum(m => m.InputTokens),
            TotalOutputTokens: byModel.Values.Sum(m => m.OutputTokens),
            TotalLlmCalls: byModel.Values.Sum(m => m.Calls),
            ByModel: byModel);
    }
}
