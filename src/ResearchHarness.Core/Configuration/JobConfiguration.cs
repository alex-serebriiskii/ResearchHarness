namespace ResearchHarness.Core;

/// <summary>
/// Configuration for a research job. Sourced from appsettings.json "Research" section.
/// JobTimeout is nullable; null resolves to a 30-minute default at validation time —
/// avoiding the TimeSpan.Zero foot-gun that would cause immediate timeout.
/// </summary>
public record JobConfiguration(
    int MaxTopics = 10,
    int MaxLabAgentsPerPI = 5,
    int MaxRevisionsPerPaper = 3,
    int PeerReviewerCount = 2,
    int SearchResultsPerQuery = 10,
    TimeSpan? JobTimeout = null,
    bool EnableConsultingFirm = true,
    string LeadModel = "claude-sonnet-4-20250514",
    string PIModel = "claude-sonnet-4-20250514",
    string LabModel = "claude-haiku-4-5-20251001",
    string ReviewerModel = "claude-sonnet-4-20250514"
)
{
    /// <summary>
    /// The effective timeout: configured value, or 30 minutes if null.
    /// </summary>
    public TimeSpan EffectiveJobTimeout => JobTimeout ?? TimeSpan.FromMinutes(30);
}
