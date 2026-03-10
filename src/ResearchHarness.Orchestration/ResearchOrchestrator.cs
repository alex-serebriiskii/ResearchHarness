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
public partial class ResearchOrchestrator : IResearchOrchestrator
{
    private readonly IInstituteLeadAgent _lead;
    private readonly IPeerReviewService _peerReviewService;
    private readonly IConsultingFirmService _consultingFirmService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobStore _jobStore;
    private readonly ITokenTracker _tokenTracker;
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
        ITokenTracker tokenTracker,
        ChannelWriter<Guid> queue,
        JobConfiguration config,
        ILogger<ResearchOrchestrator> logger)
    {
        _lead = lead;
        _peerReviewService = peerReviewService;
        _consultingFirmService = consultingFirmService;
        _serviceProvider = serviceProvider;
        _jobStore = jobStore;
        _tokenTracker = tokenTracker;
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

        LogJobCreated(_logger, job.JobId, job.Theme);

        return job.JobId;
    }

    /// <inheritdoc />
    public async Task RunJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _jobStore.GetAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found in store.");

        LogPipelineStarted(_logger, jobId, job.Theme);

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
                LogDomainBriefingRequested(_logger, jobId);
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

            LogThemeDecomposed(_logger, jobId, topics.Count);

            activity?.AddEvent(new ActivityEvent("ThemeDecomposed"));

            // Step 3: Research each topic in parallel, then peer review each paper
            var piTasks = topics.Select(async topic =>
            {
                try
                {
                    var pi = _serviceProvider.GetRequiredService<IPrincipalInvestigatorAgent>();
                    LogTopicResearching(_logger, jobId, topic.TopicId, topic.Title);

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
                    LogTopicFailed(_logger, ex, jobId, topic.TopicId);
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
            LogAssemblingJournal(_logger, jobId, papers.Count);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Assembling, ct);

            var journal = await _lead.AssembleJournalAsync(job.Theme, papers, ct);

            var costSummary = _tokenTracker.GetSummary();
            job = job with
            {
                Status = JobStatus.Completed,
                Result = journal,
                CompletedAt = DateTimeOffset.UtcNow,
                CostSummary = costSummary
            };
            await _jobStore.SaveAsync(job, ct);

            LogJobCompleted(_logger, jobId, (job.CompletedAt!.Value - job.CreatedAt).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            LogJobCancelled(_logger, jobId);
            await _jobStore.UpdateStatusAsync(jobId, JobStatus.Failed, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogJobFailed(_logger, ex, jobId);
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

    // ── Structured log methods ────────────────────────────────────────────────

    [LoggerMessage(1001, LogLevel.Information, "Research job {JobId} created and enqueued for theme: {Theme}")]
    private static partial void LogJobCreated(ILogger logger, Guid jobId, string theme);

    [LoggerMessage(1002, LogLevel.Information, "Starting research pipeline for job {JobId}: \"{Theme}\"")]
    private static partial void LogPipelineStarted(ILogger logger, Guid jobId, string theme);

    [LoggerMessage(1003, LogLevel.Information, "Job {JobId}: requesting domain briefing")]
    private static partial void LogDomainBriefingRequested(ILogger logger, Guid jobId);

    [LoggerMessage(1004, LogLevel.Information, "Job {JobId}: decomposed into {TopicCount} topic(s)")]
    private static partial void LogThemeDecomposed(ILogger logger, Guid jobId, int topicCount);

    [LoggerMessage(1005, LogLevel.Information, "Job {JobId}: PI researching topic {TopicId} \"{Title}\"")]
    private static partial void LogTopicResearching(ILogger logger, Guid jobId, Guid topicId, string title);

    [LoggerMessage(1006, LogLevel.Error, "Job {JobId}: topic {TopicId} failed")]
    private static partial void LogTopicFailed(ILogger logger, Exception ex, Guid jobId, Guid topicId);

    [LoggerMessage(1007, LogLevel.Information, "Job {JobId}: assembling journal from {PaperCount} paper(s)")]
    private static partial void LogAssemblingJournal(ILogger logger, Guid jobId, int paperCount);

    [LoggerMessage(1008, LogLevel.Information, "Job {JobId} completed successfully in {Elapsed:F1}s")]
    private static partial void LogJobCompleted(ILogger logger, Guid jobId, double elapsed);

    [LoggerMessage(1009, LogLevel.Warning, "Job {JobId} was cancelled")]
    private static partial void LogJobCancelled(ILogger logger, Guid jobId);

    [LoggerMessage(1010, LogLevel.Error, "Job {JobId} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception ex, Guid jobId);
}
