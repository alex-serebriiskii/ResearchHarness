using Microsoft.AspNetCore.Mvc;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Web.Controllers;

[ApiController]
[Route("internal/research")]
public sealed class ResearchController : ControllerBase
{
    private readonly IResearchOrchestrator _orchestrator;
    private readonly IJobStore _jobStore;

    public ResearchController(IResearchOrchestrator orchestrator, IJobStore jobStore)
    {
        _orchestrator = orchestrator;
        _jobStore = jobStore;
    }

    /// <summary>
    /// Starts a new research job for the given theme.
    /// Returns the job ID for status polling.
    /// </summary>
    [HttpPost("start")]
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
}

public record StartResearchRequest(string Theme);
