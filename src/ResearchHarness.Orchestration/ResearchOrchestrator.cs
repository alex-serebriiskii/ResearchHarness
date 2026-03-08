using System.Threading.Channels;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Orchestration;

/// <summary>
/// Implements the Phase 2 pipeline:
///   1. Optional consulting firm domain briefing
///   2. Decompose theme into topics
///   3. Research topics in parallel (one PI per topic)
///   4. Peer review + revision loop per paper
///   5. Assemble journal from accepted papers
/// </summary>
public class ResearchOrchestrator : IResearchOrchestrator
{
    private readonly IInstituteLeadAgent _lead;
    private readonly IPeerReviewService _peerReviewService;
    private readonly IConsultingFirmService _consultingFirmService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobStore _jobStore;
    private readonly ChannelWriter<Guid> _queue;
    private readonly JobConfiguration _config;
    private readonly ILogger<ResearchOrchestrator> _logger;

    private static readonly ActivitySource ActivitySource =
        new("ResearchHarness.Orchestration", "1.0.0");

    public ResearchOrchestrator(
        IInstituteLeadAgent lead,
        IPeerReviewService peerReviewService,
        IConsultingFirmService consultingFirmService,
        IServiceProvider serviceProvider,
        IJobStore jobStore,
        ChannelWriter<Guid> queue,
        JobConfiguration config,
        ILogger<ResearchOrchestrator> logger)
    {
        _lead = lead;
        _peerReviewService = peerReviewService;
        _consultingFirmService = consultingFirmService;
        _serviceProvider = serviceProvider;
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

        using var activity = ActivitySource.StartActivity(
            "RunJob",
            ActivityKind.Internal,
            parentContext: default);
        activity?.SetTag("job.id", jobId.ToString());
        activity?.SetTag("job.theme", job.Theme);

        using (_logger.BeginScope(new Dictionary<string, object> { ["JobId"] = jobId }))
        try
        {
            // Step 1: Optional consulting firm domain briefing
            string? domainContext = null;
            if (job.Config.EnableConsultingFirm)
            {
                _logger.LogInformation("Job {JobId}: requesting domain briefing", jobId);
                domainContext = await _consultingFirmService.GetDomainBriefingAsync(
                    job.Theme, "general research uncertainty", ct);
                job = job with { DomainContext = domainContext };
                await _jobStore.SaveAsync(job, ct);
            }

            activity?.AddEvent(new ActivityEvent("ConsultingBriefingComplete"));

            // Step 2: Decompose theme into topics
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Decomposing, ct);
            var topics = await _lead.DecomposeThemeAsync(job.Theme, job.Config, domainContext, ct);
            job = job with { Topics = topics, Status = JobStatus.Researching };
            await _jobStore.SaveAsync(job, ct);

            _logger.LogInformation(
                "Job {JobId}: decomposed into {TopicCount} topic(s)",
                jobId, topics.Count);

            activity?.AddEvent(new ActivityEvent("ThemeDecomposed"));

            // Step 3: Research each topic in parallel, then peer review each paper
            var piTasks = topics.Select(async topic =>
            {
                try
                {
                    var pi = _serviceProvider.GetRequiredService<IPrincipalInvestigatorAgent>();
                    _logger.LogInformation(
                        "Job {JobId}: PI researching topic {TopicId} \"{Title}\"",
                        jobId, topic.TopicId, topic.Title);

                    var paper = await pi.ResearchTopicAsync(topic, job.Config, ct);
                    paper = await RunReviewCycleAsync(pi, topic, paper, job.Config, ct);
                    return (Topic: topic, Paper: (Paper?)paper, Success: true);
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId}: topic {TopicId} failed", jobId, topic.TopicId);
                    return (Topic: topic, Paper: (Paper?)null, Success: false);
                }
            });

            var results = await Task.WhenAll(piTasks);

            activity?.AddEvent(new ActivityEvent("TopicResearchComplete"));

            // Update topics with results
            var papers = new List<Paper>();
            var updatedTopics = new List<ResearchTopic>(topics.Count);
            foreach (var result in results)
            {
                if (result.Success && result.Paper is not null)
                {
                    papers.Add(result.Paper);
                    updatedTopics.Add(result.Topic with
                    {
                        Status = TopicStatus.Completed,
                        Paper = result.Paper
                    });
                }
                else
                {
                    updatedTopics.Add(result.Topic with { Status = TopicStatus.Failed });
                }
            }
            job = job with { Topics = updatedTopics };
            await _jobStore.SaveAsync(job, ct);

            if (papers.Count == 0)
                throw new InvalidOperationException(
                    "All topics failed during research. Cannot assemble journal.");

            // Step 4: Assemble journal
            _logger.LogInformation(
                "Job {JobId}: assembling journal from {PaperCount} paper(s)",
                jobId, papers.Count);
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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Failed, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Runs the peer review + revision loop for a single paper.
    /// If PeerReviewerCount is 0, returns the paper unchanged.
    /// </summary>
    private async Task<Paper> RunReviewCycleAsync(
        IPrincipalInvestigatorAgent pi,
        ResearchTopic topic,
        Paper paper,
        JobConfiguration config,
        CancellationToken ct)
    {
        if (config.PeerReviewerCount <= 0)
            return paper;

        for (int attempt = 0; attempt <= config.MaxRevisionsPerPaper; attempt++)
        {
            var reviews = await _peerReviewService.ReviewPaperAsync(paper, topic, config, ct);
            var verdict = GetConsensusVerdict(reviews);
            paper = paper with { Reviews = reviews };

            if (verdict != ReviewVerdict.Revise || attempt == config.MaxRevisionsPerPaper)
                break;

            // Revise: re-synthesize from existing findings with feedback
            paper = await pi.ReviseTopicAsync(topic, paper, reviews, config, ct);
        }

        return paper;
    }

    /// <summary>
    /// Determines the consensus verdict from a set of reviews.
    /// Majority-rules: >50% Reject → Reject; >50% Accept → Accept; otherwise Revise.
    /// </summary>
    private static ReviewVerdict GetConsensusVerdict(List<ReviewResult> reviews)
    {
        if (reviews.Count == 0) return ReviewVerdict.Accept;

        var rejections = reviews.Count(r => r.Verdict == ReviewVerdict.Reject);
        var accepts = reviews.Count(r => r.Verdict == ReviewVerdict.Accept);

        if (rejections * 2 > reviews.Count) return ReviewVerdict.Reject;
        if (accepts * 2 > reviews.Count) return ReviewVerdict.Accept;
        return ReviewVerdict.Revise;
    }
}
