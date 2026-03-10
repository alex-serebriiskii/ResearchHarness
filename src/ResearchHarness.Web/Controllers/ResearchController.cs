using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Web.Controllers;

[ApiController]
[Route("internal/research")]
public sealed class ResearchController : ControllerBase
{
    private readonly IResearchOrchestrator _orchestrator;
    private readonly IJobStore _jobStore;
    private readonly IJobCancellationService _cancellationService;

    public ResearchController(
        IResearchOrchestrator orchestrator,
        IJobStore jobStore,
        IJobCancellationService cancellationService)
    {
        _orchestrator = orchestrator;
        _jobStore = jobStore;
        _cancellationService = cancellationService;
    }

    /// <summary>
    /// Starts a new research job for the given theme.
    /// Returns the job ID for status polling.
    /// </summary>
    [HttpPost("start")]
    [EnableRateLimiting("start-api")]
    public async Task<ActionResult<Guid>> StartJob(
        [FromBody] StartResearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Theme))
            return BadRequest("Theme must not be empty.");

        var jobId = await _orchestrator.StartResearchAsync(request.Theme, ct);
        return Ok(jobId);
    }

    /// <summary>
    /// Returns the current status of a research job.
    /// </summary>
    [HttpGet("{jobId:guid}/status")]
    public async Task<ActionResult<JobStatus>> GetStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _jobStore.GetStatusAsync(jobId, ct);
        if (status is null)
            return NotFound($"Job {jobId} not found.");

        return Ok(status);
    }

    /// <summary>
    /// Returns the completed journal for a research job.
    /// Returns 404 if the job does not exist and 409 if not yet completed.
    /// </summary>
    [HttpGet("{jobId:guid}/journal")]
    public async Task<ActionResult<Journal>> GetJournal(Guid jobId, CancellationToken ct)
    {
        var journal = await _jobStore.GetJournalAsync(jobId, ct);
        if (journal is not null)
            return Ok(journal);

        var status = await _jobStore.GetStatusAsync(jobId, ct);
        if (status is null)
            return NotFound($"Job {jobId} not found.");

        return Conflict($"Job {jobId} is not yet completed. Current status: {status}.");
    }

    /// <summary>
    /// Returns the token usage cost summary for a completed research job.
    /// Returns 404 if the job does not exist, 409 if not yet completed.
    /// </summary>
    [HttpGet("{jobId:guid}/cost")]
    public async Task<ActionResult<JobCostSummary>> GetCost(Guid jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null)
            return NotFound($"Job {jobId} not found.");

        if (job.CostSummary is null)
            return Conflict($"Job {jobId} has no cost summary. Status: {job.Status}.");

        return Ok(job.CostSummary);
    }

    /// <summary>Lists research jobs with optional status filter and pagination.</summary>
    [HttpGet("jobs")]
    public async Task<ActionResult> ListJobs(
        [FromQuery] JobStatus? status,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var (jobs, total) = await _jobStore.ListJobsAsync(offset, limit, status, ct);
        return Ok(new
        {
            jobs = jobs.Select(j => new
            {
                j.JobId,
                j.Theme,
                j.Status,
                j.CreatedAt,
                j.CompletedAt,
                TopicCount = j.Topics.Count
            }),
            total
        });
    }

    /// <summary>Cancels an in-flight research job.</summary>
    [HttpPost("{jobId:guid}/cancel")]
    public async Task<ActionResult> CancelJob(Guid jobId, CancellationToken ct)
    {
        var status = await _jobStore.GetStatusAsync(jobId, ct);
        if (status is null)
            return NotFound($"Job {jobId} not found.");

        if (status is JobStatus.Completed or JobStatus.Failed)
            return Conflict($"Job {jobId} is already {status} and cannot be cancelled.");

        var cancelled = _cancellationService.CancelJob(jobId);
        if (!cancelled)
            return Conflict($"Job {jobId} is not currently running (may be queued).");

        return Ok($"Cancellation requested for job {jobId}.");
    }
}

public record StartResearchRequest(string Theme);
