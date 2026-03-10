using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Search;

public sealed class SearchResultCache : ISearchResultCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public SearchResultCache(IMemoryCache cache, IOptions<BraveSearchOptions> options)
    {
        _cache = cache;
        _ttl = options.Value.CacheTtl;
    }

    public ValueTask<SearchResults?> GetAsync(string query, CancellationToken ct = default)
    {
        _cache.TryGetValue<SearchResults>(Key(query), out var result);
        return ValueTask.FromResult(result);
    }

    public ValueTask SetAsync(string query, SearchResults results, CancellationToken ct = default)
    {
        _cache.Set(Key(query), results, _ttl);
        return ValueTask.CompletedTask;
    }

    private static string Key(string query) => query.Trim().ToLowerInvariant();
}
