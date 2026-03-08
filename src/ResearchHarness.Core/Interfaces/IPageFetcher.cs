using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Abstraction for fetching and extracting text from a web page URL.
/// Separated from ISearchProvider because page fetching has different failure
/// modes, rate limits, and retry strategies than search API calls.
/// </summary>
public interface IPageFetcher
{
    Task<PageContent> FetchAsync(string url, CancellationToken ct = default);
}
