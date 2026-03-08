using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;
using ResearchHarness.Infrastructure.Llm;
using ResearchHarness.Infrastructure.Search.Dto;

namespace ResearchHarness.Infrastructure.Search;

public sealed class BraveSearchProvider : ISearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISearchResultCache _cache;
    private readonly BraveSearchOptions _options;
    private readonly RateLimitedExecutor _rateLimiter;
    private readonly ILogger<BraveSearchProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public BraveSearchProvider(
        IHttpClientFactory httpClientFactory,
        ISearchResultCache cache,
        IOptions<BraveSearchOptions> options,
        RateLimitedExecutor rateLimiter,
        ILogger<BraveSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<SearchResults> SearchAsync(
        string query,
        SearchOptions? options = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGet(query, out var cached))
            return cached;

        var results = await _rateLimiter.ExecuteSearchCallAsync(
            () => FetchFromApiAsync(query, options, ct), ct);

        _cache.Set(query, results);
        return results;
    }

    private async Task<SearchResults> FetchFromApiAsync(
        string query,
        SearchOptions? options,
        CancellationToken ct)
    {
        var count = Math.Clamp(options?.Count ?? 10, 1, 20);
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{_options.BaseUrl}/res/v1/web/search?q={encodedQuery}&count={count}&result_filter=web";

        using var client = _httpClientFactory.CreateClient("BraveSearch");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Subscription-Token", _options.ApiKey);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Brave search returned {StatusCode} for query {Query}",
                response.StatusCode, query);
            return new SearchResults([], null);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var dto = JsonSerializer.Deserialize<BraveSearchResponse>(json, JsonOptions);

        var hits = dto?.Web?.Results
            .Select(r => new SearchHit(
                r.Url,
                r.Title,
                r.Description ?? "",
                DateTimeOffset.TryParse(r.Age, out var date) ? date : null))
            .ToList() ?? [];

        return new SearchResults(hits, null);
    }
}
