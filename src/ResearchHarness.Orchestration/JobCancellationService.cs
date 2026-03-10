using System.Collections.Concurrent;
using ResearchHarness.Core.Interfaces;

namespace ResearchHarness.Orchestration;

public sealed class JobCancellationService : IJobCancellationService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    public CancellationToken RegisterJob(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        _jobs[jobId] = cts;
        return cts.Token;
    }

    public bool CancelJob(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void CompleteJob(Guid jobId)
    {
        if (_jobs.TryRemove(jobId, out var cts))
            cts.Dispose();
    }

    public bool IsRegistered(Guid jobId) => _jobs.ContainsKey(jobId);
}
