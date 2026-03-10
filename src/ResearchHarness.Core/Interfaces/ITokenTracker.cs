using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

public interface ITokenTracker
{
    void Record(string model, int inputTokens, int outputTokens);
    JobCostSummary GetSummary();
}
