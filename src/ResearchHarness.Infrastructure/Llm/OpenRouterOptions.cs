namespace ResearchHarness.Infrastructure.Llm;

public class OpenRouterOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api";
    public int MaxConcurrentLlmCalls { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    /// <summary>
    /// Minimum seconds to wait before retrying a 429 rate-limit response.
    /// Grows linearly per attempt: 1×base, 2×base, 3×base, capped at 120 s.
    /// Free-tier upstream providers can impose multi-minute rate limits, so the
    /// default is intentionally much larger than the exponential backoff used for
    /// 5xx errors. Set to 0 in tests to avoid real waits.
    /// </summary>
    public double RateLimitRetryBaseDelaySeconds { get; set; } = 20.0;
    /// <summary>
    /// Sent as the HTTP-Referer header. OpenRouter uses this for usage attribution
    /// on the OpenRouter leaderboard. Optional but recommended.
    /// </summary>
    public string SiteUrl { get; set; } = "";
    /// <summary>
    /// Sent as the X-Title header. Appears on the OpenRouter dashboard.
    /// </summary>
    public string SiteName { get; set; } = "";
    /// <summary>
    /// Maps primary model identifiers to fallback models used when a 429
    /// rate-limit response is received without a Retry-After header.
    /// Key: primary model; Value: fallback model.
    /// </summary>
    public Dictionary<string, string> FallbackModels { get; set; } = [];
}
