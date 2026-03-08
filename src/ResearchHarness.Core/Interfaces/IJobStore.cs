using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Persistence layer for research jobs.
/// Phase 1: backed by InMemoryJobStore.
/// Phase 2+: backed by EF Core + SQL.
/// </summary>
public interface IJobStore
{
    Task SaveAsync(ResearchJob job, CancellationToken ct = default);
    Task<ResearchJob?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default);
    Task<Journal?> GetJournalAsync(Guid jobId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid jobId, JobStatus status, CancellationToken ct = default);
}
