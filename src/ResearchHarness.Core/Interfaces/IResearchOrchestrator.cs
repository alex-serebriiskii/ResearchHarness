namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Entry point for the research pipeline.
/// Creates a job, enqueues it for background processing, and returns the job ID.
/// </summary>
public interface IResearchOrchestrator
{
    /// <summary>
    /// Enqueues a new research job and returns its ID immediately.
    /// The job runs asynchronously via ResearchJobProcessor.
    /// </summary>
    Task<Guid> StartResearchAsync(string theme, CancellationToken ct = default);

    /// <summary>
    /// Executes the full pipeline synchronously (called by ResearchJobProcessor).
    /// </summary>
    Task RunJobAsync(Guid jobId, CancellationToken ct = default);
}
