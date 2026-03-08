using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Search;

/// <summary>
/// ISearchResultCache backed by IDistributedCache (e.g. Redis).
/// Falls back gracefully: cache misses or deserialization failures return false
/// without crashing the search pipeline.
/// </summary>
public sealed class DistributedSearchResultCache : ISearchResultCache
{
    private readonly IDistributedCache _cache;
    private readonly DistributedCacheEntryOptions _entryOptions;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DistributedSearchResultCache(
        IDistributedCache cache,
        IOptions<BraveSearchOptions> options)
    {
        _cache = cache;
        _entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.Value.CacheTtl
        };
    }

    public bool TryGet(string query, [NotNullWhen(true)] out SearchResults? results)
    {
        // IDistributedCache is synchronous-friendly via GetAsync but TryGet is sync.
        // Use the synchronous Get overload to preserve the interface contract.
        var bytes = _cache.Get(Key(query));
        if (bytes is null)
        {
            results = null;
            return false;
        }

        try
        {
            results = JsonSerializer.Deserialize<SearchResults>(bytes, JsonOpts);
            return results is not null;
        }
        catch (JsonException)
        {
            results = null;
            return false;
        }
    }

    public void Set(string query, SearchResults results)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(results, JsonOpts);
        _cache.Set(Key(query), bytes, _entryOptions);
    }

    private static string Key(string query) => $"brave_search:{query.Trim().ToLowerInvariant()}";
}
