namespace ResearchHarness.Core.Models;

/// <summary>
/// Structured results returned by ISearchProvider.SearchAsync.
/// </summary>
public record SearchResults(
    List<SearchHit> Hits,
    string? NextPageToken
);

public record SearchHit(
    string Url,
    string Title,
    string Snippet,
    DateTimeOffset? PublishedDate
);

/// <summary>
/// Full page content fetched by IPageFetcher.FetchAsync.
/// </summary>
public record PageContent(
    string Url,
    string RawText,
    string? Title,
    DateTimeOffset? PublishedDate
);

/// <summary>
/// Options forwarded to the search provider.
/// </summary>
public record SearchOptions(
    int Count = 10,
    string? Language = null,
    string? Country = null,
    DateTimeOffset? PublishedAfter = null
);
