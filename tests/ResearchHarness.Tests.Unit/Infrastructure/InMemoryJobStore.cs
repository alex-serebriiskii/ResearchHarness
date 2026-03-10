using System.Collections.Concurrent;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Tests.Unit.Infrastructure;

/// <summary>
/// In-memory IJobStore implementation for use in tests.
/// Not for production use — SqliteJobStore is the production implementation.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, ResearchJob> _jobs = new();

    public Task SaveAsync(ResearchJob job, CancellationToken ct = default)
    {
        _jobs[job.JobId] = job;
        return Task.CompletedTask;
    }

    public Task<ResearchJob?> GetAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job : null);

    public Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job.Status : (JobStatus?)null);

    public Task<Journal?> GetJournalAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job.Result : null);

    public Task UpdateStatusAsync(Guid jobId, JobStatus status, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            _jobs[jobId] = job with { Status = status };
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<ResearchJob> Jobs, int Total)> ListJobsAsync(
        int offset = 0, int limit = 20, JobStatus? status = null, CancellationToken ct = default)
    {
        var all = _jobs.Values
            .Where(j => status is null || j.Status == status)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
        IReadOnlyList<ResearchJob> page = all.Skip(offset).Take(limit).ToList();
        return Task.FromResult((page, all.Count));
    }

    public Task<JobCostSummary?> GetCostAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job.CostSummary : null);
}
