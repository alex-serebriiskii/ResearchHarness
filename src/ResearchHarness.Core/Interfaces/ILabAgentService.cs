using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Lab agent: executes a single search task and returns structured findings.
/// Wraps ISearchProvider + IPageFetcher + weak LLM extraction.
/// </summary>
public interface ILabAgentService
{
    Task<List<Finding>> ExecuteSearchTaskAsync(
        SearchTask task,
        JobConfiguration config,
        CancellationToken ct = default);
}
