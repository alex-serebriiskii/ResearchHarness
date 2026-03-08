namespace ResearchHarness.Infrastructure.Llm;

public class OpenRouterOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api";
    public int MaxConcurrentLlmCalls { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    /// <summary>
    /// Sent as the HTTP-Referer header. OpenRouter uses this for usage attribution
    /// on the OpenRouter leaderboard. Optional but recommended.
    /// </summary>
    public string SiteUrl { get; set; } = "";
    /// <summary>
    /// Sent as the X-Title header. Appears on the OpenRouter dashboard.
    /// </summary>
    public string SiteName { get; set; } = "";
}
