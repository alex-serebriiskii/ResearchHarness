namespace ResearchHarness.Client;

public record JobListResult(List<JobListEntry> Jobs, int Total);

public record JobListEntry(
    Guid JobId,
    string Theme,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int TopicCount);
