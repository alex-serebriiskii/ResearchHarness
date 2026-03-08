namespace ResearchHarness.Infrastructure.Search;

public class BraveSearchOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.search.brave.com";
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);
    public int PageFetchTimeoutSeconds { get; set; } = 15;
}
