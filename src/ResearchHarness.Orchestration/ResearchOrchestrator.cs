using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Orchestration;

/// <summary>
/// Implements the Phase 1 linear pipeline:
/// Decompose → Research (1 topic) → Assemble journal.
/// Peer review and consulting are skipped in Phase 1.
/// </summary>
public class ResearchOrchestrator : IResearchOrchestrator
{
    private readonly IInstituteLeadAgent _lead;
    private readonly IPrincipalInvestigatorAgent _pi;
    private readonly IJobStore _jobStore;
    private readonly ChannelWriter<Guid> _queue;
    private readonly JobConfiguration _config;
    private readonly ILogger<ResearchOrchestrator> _logger;

    public ResearchOrchestrator(
        IInstituteLeadAgent lead,
        IPrincipalInvestigatorAgent pi,
        IJobStore jobStore,
        ChannelWriter<Guid> queue,
        JobConfiguration config,
        ILogger<ResearchOrchestrator> logger)
    {
        _lead = lead;
        _pi = pi;
        _jobStore = jobStore;
        _queue = queue;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> StartResearchAsync(string theme, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(theme))
            throw new ArgumentException("Theme must not be empty.", nameof(theme));

        var job = new ResearchJob(
            JobId: Guid.NewGuid(),
            Theme: theme.Trim(),
            DomainContext: null,
            Status: JobStatus.Pending,
            Topics: [],
            Result: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            Config: _config
        );

        await _jobStore.SaveAsync(job, ct);
        await _queue.WriteAsync(job.JobId, ct);

        _logger.LogInformation(
            "Research job {JobId} created and enqueued for theme: {Theme}",
            job.JobId, job.Theme);

        return job.JobId;
    }

    /// <inheritdoc />
    public async Task RunJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _jobStore.GetAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found in store.");

        _logger.LogInformation(
            "Starting research pipeline for job {JobId}: \"{Theme}\"",
            jobId, job.Theme);

        try
        {
            // Step 1: Decompose theme into topics
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Decomposing, ct);

            var topics = await _lead.DecomposeThemeAsync(job.Theme, job.Config, ct);

            job = job with { Topics = topics, Status = JobStatus.Researching };
            await _jobStore.SaveAsync(job, ct);

            _logger.LogInformation(
                "Job {JobId}: decomposed into {TopicCount} topic(s)",
                jobId, topics.Count);

            // Step 2: Research each topic (Phase 1: exactly 1)
            var papers = new List<Paper>();

            foreach (var topic in topics)
            {
                _logger.LogInformation(
                    "Job {JobId}: PI researching topic {TopicId} \"{Title}\"",
                    jobId, topic.TopicId, topic.Title);

                var paper = await _pi.ResearchTopicAsync(topic, job.Config, ct);
                papers.Add(paper);

                // Reflect paper back onto the topic in the job record
                var updatedTopics = job.Topics
                    .Select(t => t.TopicId == topic.TopicId
                        ? t with { Status = TopicStatus.Completed, Paper = paper }
                        : t)
                    .ToList();

                job = job with { Topics = updatedTopics };
                await _jobStore.SaveAsync(job, ct);
            }

            // Step 3: Assemble journal (peer review skipped in Phase 1)
            _logger.LogInformation("Job {JobId}: assembling journal", jobId);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Assembling, ct);

            var journal = await _lead.AssembleJournalAsync(job.Theme, papers, ct);

            job = job with
            {
                Status = JobStatus.Completed,
                Result = journal,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _jobStore.SaveAsync(job, ct);

            _logger.LogInformation(
                "Job {JobId} completed successfully in {Elapsed:F1}s",
                jobId,
                (job.CompletedAt!.Value - job.CreatedAt).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} was cancelled", jobId);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Failed, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Failed, CancellationToken.None);
            throw;
        }
    }
}
