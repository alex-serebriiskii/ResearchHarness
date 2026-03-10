using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Persistence layer for research jobs.
/// </summary>
public interface IJobStore
{
    Task SaveAsync(ResearchJob job, CancellationToken ct = default);
    Task<ResearchJob?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default);
    Task<Journal?> GetJournalAsync(Guid jobId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid jobId, JobStatus status, CancellationToken ct = default);
    Task<(IReadOnlyList<ResearchJob> Jobs, int Total)> ListJobsAsync(
        int offset = 0, int limit = 20, JobStatus? status = null, CancellationToken ct = default);
    Task<JobCostSummary?> GetCostAsync(Guid jobId, CancellationToken ct = default);
}
