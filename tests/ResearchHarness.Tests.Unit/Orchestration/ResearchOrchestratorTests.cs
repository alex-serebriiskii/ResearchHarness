using System.Threading.Channels;
using AwesomeAssertions;
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
    private IJobStore _jobStore = null!;
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
        _jobStore = Substitute.For<IJobStore>();
        _channel = Channel.CreateUnbounded<Guid>();
        _config = new JobConfiguration(MaxTopics: 1);

        _orchestrator = new ResearchOrchestrator(
            _lead,
            _pi,
            _jobStore,
            _channel.Writer,
            _config,
            Substitute.For<ILogger<ResearchOrchestrator>>()
        );
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

        _jobStore.GetAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(job);
        _lead.DecomposeThemeAsync("theme", _config, Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(SampleTopic, _config, Arg.Any<CancellationToken>())
            .Returns(paper);
        _lead.AssembleJournalAsync("theme", Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(journal);

        await _orchestrator.RunJobAsync(jobId);

        // Status should transition through the pipeline
        await _jobStore.Received().UpdateStatusAsync(jobId, JobStatus.Decomposing, Arg.Any<CancellationToken>());
        await _jobStore.Received().UpdateStatusAsync(jobId, JobStatus.Assembling, Arg.Any<CancellationToken>());

        // Final save should be Completed
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
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
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
        _lead.DecomposeThemeAsync(Arg.Any<string>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns([SampleTopic]);
        _pi.ResearchTopicAsync(Arg.Any<ResearchTopic>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(paper);
        _lead.AssembleJournalAsync(Arg.Any<string>(), Arg.Any<List<Paper>>(), Arg.Any<CancellationToken>())
            .Returns(journal);

        await _orchestrator.RunJobAsync(jobId);

        // At least one intermediate save should show the topic with status Completed
        await _jobStore.Received().SaveAsync(
            Arg.Is<ResearchJob>(j =>
                j.Topics.Any(t => t.Status == TopicStatus.Completed && t.Paper != null)),
            Arg.Any<CancellationToken>());
    }
}
