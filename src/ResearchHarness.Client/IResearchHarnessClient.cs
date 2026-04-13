using ResearchHarness.Core.Models;

namespace ResearchHarness.Client;

/// <summary>
/// Typed client for the ResearchHarness API.
/// </summary>
public interface IResearchHarnessClient : IAsyncDisposable
{
    Task<Guid> StartJobAsync(string theme, CancellationToken ct = default);
    Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default);
    Task<Journal> GetJournalAsync(Guid jobId, CancellationToken ct = default);
    Task<JobCostSummary> GetCostAsync(Guid jobId, CancellationToken ct = default);
    Task CancelJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobListResult> ListJobsAsync(JobStatus? status = null, int offset = 0, int limit = 20, CancellationToken ct = default);
}
