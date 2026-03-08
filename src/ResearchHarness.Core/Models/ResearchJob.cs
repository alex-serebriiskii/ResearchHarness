namespace ResearchHarness.Core.Models;

public record ResearchJob(
    Guid JobId,
    string Theme,
    string? DomainContext,
    JobStatus Status,
    List<ResearchTopic> Topics,
    Journal? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    JobConfiguration Config
);
