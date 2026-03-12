using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Called by the orchestrator at each pipeline phase transition to broadcast progress.
/// </summary>
public interface IJobProgressNotifier
{
    Task NotifyAsync(JobProgressEvent progress, CancellationToken ct = default);
}
