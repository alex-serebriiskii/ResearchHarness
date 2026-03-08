using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Abstraction over a web search API.
/// Responsible only for issuing queries and returning structured hits.
/// Page fetching is handled by IPageFetcher.
/// </summary>
public interface ISearchProvider
{
    Task<SearchResults> SearchAsync(
        string query,
        SearchOptions? options = null,
        CancellationToken ct = default);
}
