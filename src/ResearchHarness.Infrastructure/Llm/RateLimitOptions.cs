namespace ResearchHarness.Infrastructure.Llm;

/// <summary>
/// Configuration for concurrency limits on LLM and search API calls.
/// Bound from the "RateLimit" configuration section.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Maximum concurrent LLM API calls. Default: 10.</summary>
    public int LlmConcurrency { get; set; } = 10;

    /// <summary>Maximum concurrent search API calls. Default: 5.</summary>
    public int SearchConcurrency { get; set; } = 5;
}
