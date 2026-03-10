using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Search;

public interface ISearchResultCache
{
    ValueTask<SearchResults?> GetAsync(string query, CancellationToken ct = default);
    ValueTask SetAsync(string query, SearchResults results, CancellationToken ct = default);
}
