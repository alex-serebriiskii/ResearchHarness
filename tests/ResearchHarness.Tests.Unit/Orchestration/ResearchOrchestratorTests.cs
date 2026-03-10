using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;
using ResearchHarness.Orchestration;

namespace ResearchHarness.Tests.Unit.Orchestration;

public class ResearchOrchestratorTests
{
    private IInstituteLeadAgent _lead = null!;
    private IPrincipalInvestigatorAgent _pi = null!;
    private IPeerReviewService _peerReviewService = null!;
    private IConsultingFirmService _consultingFirmService = null!;
    private IServiceProvider _serviceProvider = null!;
    private IJobStore _jobStore = null!;
    private ITokenTracker _tokenTracker = null!;
    private Channel<Guid> _channel = null!;
    private ResearchOrchestrator _orchestrator = null!;
    private JobConfiguration _config = null!;

    private static readonly ResearchTopic SampleTopic = new(
        TopicId: Guid.NewGuid(),
        Title: "Test Topic",
        Scope: "Test scope",
        SuggestedSearchAngles: ["angle1"],
        ExpectedSourceTypes: ["news"],
        Status: TopicStatus.Pending,
        Paper: null
    );

    private static Paper BuildPaper(Guid topicId) => new(
        TopicId: topicId,
        ExecutiveSummary: "Test summary",
        Findings: [],
        Bibliography: [],
        ConfidenceScore: 0.8,
        RevisionCount: 0,
        Reviews: []
    );

    private static Journal BuildJournal() => new(
        OverallSummary: "overall",
        CrossTopicAnalysis: "cross",
        Papers: [],
        MasterBibliography: [],
        AssembledAt: DateTimeOffset.UtcNow
    );

    [Before(Test)]
    public void Setup()
    {
        _lead = Substitute.For<IInstituteLeadAgent>();
        _pi = Substitute.For<IPrincipalInvestigatorAgent>();
        _peerReviewService = Substitute.For<IPeerReviewService>();
        _consultingFirmService = Substitute.For<IConsultingFirmService>();
        _jobStore = Substitute.For<IJobStore>();
        _tokenTracker = Substitute.For<ITokenTracker>();
        _tokenTracker.GetSummary().Returns(new JobCostSummary(0, 0, 0, []));
        _channel = Channel.CreateUnbounded<Guid>();

        // Config: peer review and consulting disabled for basic tests
        _config = new JobConfiguration(MaxTopics: 1, PeerReviewerCount: 0, EnableConsultingFirm: false);

        // IServiceProvider resolves IPrincipalInvestigatorAgent to _pi
        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(typeof(IPrincipalInvestigatorAgent)).Returns(_pi);

        _orchestrator = new ResearchOrchestrator(
            _lead,
            _peerReviewService,
            _consultingFirmService,
            _serviceProvider,
            _jobStore,
            _tokenTracker,
            _channel.Writer,
            _config,
            Substitute.For<ILogger<ResearchOrchestrator>>())
        ;
    }

    [Test]
    public async Task StartResearchAsync_SavesJobAndEnqueues()
    {
        var jobId = await _orchestrator.StartResearchAsync("test theme");

        await _jobStore.Received(1).SaveAsync(
            Arg.Is<ResearchJob>(j => j.Theme == "test theme" && j.Status == JobStatus.Pending),
            Arg.Any<CancellationToken>());

        _channel.Reader.TryRead(out var enqueuedId).Should().BeTrue();
        enqueuedId.Should().Be(jobId);
    }

    [Test]
    public async Task StartResearchAsync_EmptyTheme_ThrowsArgumentException()
    {
        Func<Task> act = () => _orchestrator.StartResearchAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task RunJobAsync_HappyPath_CompletesJob()
    {
        var paper = BuildPaper(SampleTopic.TopicId);
        var journal = BuildJournal();
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(
            jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, _config);

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync("theme", _config, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(SampleTopic, _config, Arg.Any<CancellationToken>())
            .Returns(paper);
        _lead.AssembleJournalAsync("theme", Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(journal);

        await _orchestrator.RunJobAsync(jobId);

        await _jobStore.Received().UpdateStatusAsync(jobId, JobStatus.Decomposing, Arg.Any<CancellationToken>());
        await _jobStore.Received().UpdateStatusAsync(jobId, JobStatus.Assembling, Arg.Any<CancellationToken>());
        await _jobStore.Received().SaveAsync(
            Arg.Is<ResearchJob>(j => j.Status == JobStatus.Completed && j.Result != null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_JobNotFound_Throws()
    {
        _jobStore.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ResearchJob?)null);

        Func<Task> act = () => _orchestrator.RunJobAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RunJobAsync_LlmFails_MarksJobFailed()
    {
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(
            jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, _config);

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(
                Arg.Any<string>(), Arg.Any<JobConfiguration>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("LLM failed"));

        Func<Task> act = () => _orchestrator.RunJobAsync(jobId);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await _jobStore.Received().UpdateStatusAsync(
            jobId, JobStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_PaperIsAttachedToTopic()
    {
        var paper = BuildPaper(SampleTopic.TopicId);
        var journal = BuildJournal();
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(
            jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, _config);

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(paper);
        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(journal);

        await _orchestrator.RunJobAsync(jobId);

        await _jobStore.Received().SaveAsync(
            Arg.Is<ResearchJob>(j =>
                j.Topics.Any(t => t.Status == TopicStatus.Completed && t.Paper != null)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_ParallelTopics_AllResearched()
    {
        var topic1 = SampleTopic;
        var topic2 = SampleTopic with { TopicId = Guid.NewGuid(), Title = "Topic 2" };
        var config = new JobConfiguration(MaxTopics: 2, PeerReviewerCount: 0, EnableConsultingFirm: false);
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, config);

        // New orchestrator with MaxTopics=2 config
        var orchestrator = new ResearchOrchestrator(
                    _lead, _peerReviewService, _consultingFirmService,
                    _serviceProvider, _jobStore, _tokenTracker, _channel.Writer, config,
                    Substitute.For<ILogger<ResearchOrchestrator>>());

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([topic1, topic2]);
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(ci => BuildPaper(ci.Arg<ResearchTopic>().TopicId));
        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(BuildJournal());

        await orchestrator.RunJobAsync(jobId);

        // PI should be invoked twice (once per topic)
        await _pi.Received(2).ResearchTopicAsync(
            Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_EnableConsultingFirm_CallsConsultingService()
    {
        var config = new JobConfiguration(MaxTopics: 1, PeerReviewerCount: 0, EnableConsultingFirm: true);
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, config);

        var orchestrator = new ResearchOrchestrator(
                    _lead, _peerReviewService, _consultingFirmService,
                    _serviceProvider, _jobStore, _tokenTracker, _channel.Writer, config,
                    Substitute.For<ILogger<ResearchOrchestrator>>());

        _consultingFirmService.GetDomainBriefingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("domain briefing content");
        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(BuildPaper(SampleTopic.TopicId));
        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(BuildJournal());

        await orchestrator.RunJobAsync(jobId);

        await _consultingFirmService.Received(1).GetDomainBriefingAsync(
            "theme", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_PeerReview_AcceptedPaper_NoRevision()
    {
        var config = new JobConfiguration(MaxTopics: 1, PeerReviewerCount: 1, EnableConsultingFirm: false);
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, config);

        var orchestrator = new ResearchOrchestrator(
                    _lead, _peerReviewService, _consultingFirmService,
                    _serviceProvider, _jobStore, _tokenTracker, _channel.Writer, config,
                    Substitute.For<ILogger<ResearchOrchestrator>>());

        var acceptReview = new ReviewResult(ReviewVerdict.Accept, "Good paper", [], DateTimeOffset.UtcNow);
        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(BuildPaper(SampleTopic.TopicId));
        _peerReviewService.ReviewPaperAsync(Arg.Any<Paper>(), Arg.Any<ResearchTopic>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns([acceptReview]);
        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(BuildJournal());

        await orchestrator.RunJobAsync(jobId);

        // Review called once; no revision needed
        await _peerReviewService.Received(1).ReviewPaperAsync(
            Arg.Any<Paper>(), Arg.Any<ResearchTopic>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
        await _pi.DidNotReceive().ReviseTopicAsync(
            Arg.Any<ResearchTopic>(), Arg.Any<Paper>(), Arg.Any<List<ReviewResult>>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_PeerReview_RevisionLoop_RevisesAndAccepts()
    {
        var config = new JobConfiguration(MaxTopics: 1, PeerReviewerCount: 1,
            MaxRevisionsPerPaper: 2, EnableConsultingFirm: false);
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, config);

        var orchestrator = new ResearchOrchestrator(
                    _lead, _peerReviewService, _consultingFirmService,
                    _serviceProvider, _jobStore, _tokenTracker, _channel.Writer, config,
                    Substitute.For<ILogger<ResearchOrchestrator>>());

        var reviseReview = new ReviewResult(ReviewVerdict.Revise, "Needs more clarity", ["Add sources"], DateTimeOffset.UtcNow);
        var acceptReview = new ReviewResult(ReviewVerdict.Accept, "Improved", [], DateTimeOffset.UtcNow);

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);

        var initialPaper = BuildPaper(SampleTopic.TopicId);
        var revisedPaper = initialPaper with { RevisionCount = 1 };
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(initialPaper);
        _pi.ReviseTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<Paper>(), Arg.Any<List<ReviewResult>>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(revisedPaper);

        // First review: Revise; second review: Accept
        _peerReviewService.ReviewPaperAsync(Arg.Any<Paper>(), Arg.Any<ResearchTopic>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns([reviseReview], [acceptReview]);

        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(BuildJournal());

        await orchestrator.RunJobAsync(jobId);

        // Review called twice; revision called once
        await _peerReviewService.Received(2).ReviewPaperAsync(
            Arg.Any<Paper>(), Arg.Any<ResearchTopic>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
        await _pi.Received(1).ReviseTopicAsync(
            Arg.Any<ResearchTopic>(), Arg.Any<Paper>(), Arg.Any<List<ReviewResult>>(),
            Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunJobAsync_TopicFailure_DoesNotAbortOtherTopics()
    {
        var topic1 = SampleTopic;
        var topic2 = SampleTopic with { TopicId = Guid.NewGuid(), Title = "Topic 2" };
        var config = new JobConfiguration(MaxTopics: 2, PeerReviewerCount: 0, EnableConsultingFirm: false);
        var jobId = Guid.NewGuid();
        var job = new ResearchJob(jobId, "theme", null, JobStatus.Pending,
            [], null, DateTimeOffset.UtcNow, null, config);

        var orchestrator = new ResearchOrchestrator(
                    _lead, _peerReviewService, _consultingFirmService,
                    _serviceProvider, _jobStore, _tokenTracker, _channel.Writer, config,
                    Substitute.For<ILogger<ResearchOrchestrator>>());

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>()).Returns(job);
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([topic1, topic2]);

        // topic1 succeeds; topic2 throws
        _pi.ResearchTopicAsync(
                Arg.Is<ResearchTopic>(t => t.TopicId == topic1.TopicId),
                Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(BuildPaper(topic1.TopicId));
        _pi.ResearchTopicAsync(
                Arg.Is<ResearchTopic>(t => t.TopicId == topic2.TopicId),
                Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("PI failed for topic 2"));

        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(BuildJournal());

        // Should NOT throw — partial completion is acceptable
        await orchestrator.RunJobAsync(jobId);

        // topic1 paper is assembled; topic2 is skipped
        await _lead.Received(1).AssembleJournalAsync(
            Arg.Any<string>(),
            Arg.Is<List<Paper>>(p => p.Count == 1),
            Arg.Any<CancellationToken>());
    }
}
