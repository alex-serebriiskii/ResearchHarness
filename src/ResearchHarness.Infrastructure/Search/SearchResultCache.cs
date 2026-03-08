using System.Diagnostics.CodeAnalysis;
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

    public bool TryGet(string query, [NotNullWhen(true)] out SearchResults? results)
    {
        if (_cache.TryGetValue<SearchResults>(Key(query), out var value) && value is not null)
        {
            results = value;
            return true;
        }

        results = null;
        return false;
    }

    public void Set(string query, SearchResults results) =>
        _cache.Set(Key(query), results, _ttl);

    private static string Key(string query) => query.Trim().ToLowerInvariant();
}
