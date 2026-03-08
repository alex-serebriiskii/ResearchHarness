namespace ResearchHarness.Core.Models;

public record ReviewResult(
    ReviewVerdict Verdict,
    string Feedback,
    List<string> Issues,
    DateTimeOffset ReviewedAt
);
