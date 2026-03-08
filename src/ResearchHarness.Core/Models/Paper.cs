namespace ResearchHarness.Core.Models;

/// <summary>
/// Synthesized research paper produced by a Principal Investigator.
/// TopicId ties this paper to its originating ResearchTopic for downstream phases.
/// </summary>
public record Paper(
    Guid TopicId,
    string ExecutiveSummary,
    List<Finding> Findings,
    List<Source> Bibliography,
    double ConfidenceScore,
    int RevisionCount,
    List<ReviewResult> Reviews
);
