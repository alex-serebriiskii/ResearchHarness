using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Search;

/// <summary>
/// ISearchResultCache backed by IDistributedCache (e.g. Redis).
/// Falls back gracefully: cache misses or deserialization failures return null
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

    public async ValueTask<SearchResults?> GetAsync(string query, CancellationToken ct = default)
    {
        var bytes = await _cache.GetAsync(Key(query), ct);
        if (bytes is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<SearchResults>(bytes, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask SetAsync(string query, SearchResults results, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(results, JsonOpts);
        await _cache.SetAsync(Key(query), bytes, _entryOptions, ct);
    }

    private static string Key(string query) => $"brave_search:{query.Trim().ToLowerInvariant()}";
}
