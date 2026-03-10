namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Manages per-job cancellation tokens, allowing in-flight jobs to be cancelled
/// independently of the host shutdown token.
/// </summary>
public interface IJobCancellationService
{
    /// <summary>Registers a new job and returns a token that fires when the job is cancelled.</summary>
    CancellationToken RegisterJob(Guid jobId);

    /// <summary>Cancels the job if it is currently registered. Returns true if cancelled.</summary>
    bool CancelJob(Guid jobId);

    /// <summary>Removes and disposes the per-job CTS when the job finishes.</summary>
    void CompleteJob(Guid jobId);

    /// <summary>Returns true if the given job is currently registered (in-flight).</summary>
    bool IsRegistered(Guid jobId);
}
