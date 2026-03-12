namespace ResearchHarness.Core.Models;

/// <summary>
/// Represents a single progress notification emitted by the orchestrator.
/// </summary>
public record JobProgressEvent(
    Guid JobId,
    JobStatus Status,
    string Message,
    int CompletedTopics,
    int TotalTopics,
    JobCostSummary? CostSnapshot,
    DateTimeOffset Timestamp);
