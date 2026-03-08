using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ResearchHarness.Agents;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Tests.Unit.Agents;

public class PrincipalInvestigatorAgentTests
{
    private ILlmClient _llm = null!;
    private ILabAgentServiceInternal _labAgent = null!;
    private PrincipalInvestigatorAgent _pi = null!;
    private JobConfiguration _config = null!;

    private static readonly ResearchTopic Topic = new(
        TopicId: Guid.NewGuid(),
        Title: "AI Drug Discovery",
        Scope: "Application of AI in drug discovery pipelines",
        SuggestedSearchAngles: ["ML for molecular design", "clinical trials AI"],
        ExpectedSourceTypes: ["academic", "industry"],
        Status: TopicStatus.Pending,
        Paper: null
    );

    [Before(Test)]
    public void Setup()
    {
        _llm = Substitute.For<ILlmClient>();
        _labAgent = Substitute.For<ILabAgentServiceInternal>();
        _config = new JobConfiguration(MaxLabAgentsPerPI: 3, PIModel: "claude-test-pi", LabModel: "claude-test-lab");

        _pi = new PrincipalInvestigatorAgent(
            _llm,
            _labAgent,
            Substitute.For<ILogger<PrincipalInvestigatorAgent>>()
        );
    }

    private void SetupTaskBreakdown(int taskCount = 2)
    {
        var tasks = Enumerable.Range(1, taskCount).Select(i => new SearchTaskDto(
            $"query {i}",
            ["news", "academic"],
            $"extract info {i}",
            $"relevance criteria {i}"
        )).ToList();

        var output = new TaskBreakdownOutput(tasks);
        _llm.CompleteAsync<TaskBreakdownOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<TaskBreakdownOutput>(output, new TokenUsage(100, 50), "tool_use"));
    }

    private void SetupLabAgent(int findingsPerTask = 1)
    {
        var finding = new Finding("subtopic", "summary", ["kp1"], [Guid.NewGuid()], 0.8);
        var source = new Source(Guid.NewGuid(), "https://example.com", "Example", null, null,
            SourceCredibility.High, "reputable");
        var result = new LabTaskResult([finding], [source]);

        _labAgent.ExecuteSearchTaskFullAsync(
                Arg.Any<SearchTask>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(result);
    }

    private void SetupSynthesis()
    {
        var output = new PaperSynthesisOutput("Executive summary of findings", 0.85);
        _llm.CompleteAsync<PaperSynthesisOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<PaperSynthesisOutput>(output, new TokenUsage(200, 300), "tool_use"));
    }

    [Test]
    public async Task ResearchTopicAsync_ReturnsExpectedPaper()
    {
        SetupTaskBreakdown();
        SetupLabAgent();
        SetupSynthesis();

        var paper = await _pi.ResearchTopicAsync(Topic, _config);

        paper.TopicId.Should().Be(Topic.TopicId);
        paper.ExecutiveSummary.Should().Be("Executive summary of findings");
        paper.ConfidenceScore.Should().BeApproximately(0.85, 0.001);
        paper.RevisionCount.Should().Be(0);
        paper.Reviews.Should().BeEmpty();
    }

    [Test]
    public async Task ResearchTopicAsync_ExecutesLabAgentForEachTask()
    {
        const int taskCount = 3;
        SetupTaskBreakdown(taskCount);
        SetupLabAgent();
        SetupSynthesis();

        await _pi.ResearchTopicAsync(Topic, _config);

        await _labAgent.Received(taskCount).ExecuteSearchTaskFullAsync(
            Arg.Any<SearchTask>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResearchTopicAsync_UsesPIModelForBreakdownAndSynthesis()
    {
        SetupTaskBreakdown();
        SetupLabAgent();
        SetupSynthesis();

        await _pi.ResearchTopicAsync(Topic, _config);

        // Both breakdown and synthesis LLM calls must use PIModel
        await _llm.Received().CompleteAsync<TaskBreakdownOutput>(
            Arg.Is<LlmRequest>(r => r.Model == _config.PIModel),
            Arg.Any<CancellationToken>());
        await _llm.Received().CompleteAsync<PaperSynthesisOutput>(
            Arg.Is<LlmRequest>(r => r.Model == _config.PIModel),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResearchTopicAsync_RespectsMaxLabAgentsPerPI()
    {
        // Breakdown returns more tasks than MaxLabAgentsPerPI
        SetupTaskBreakdown(taskCount: 10);
        SetupLabAgent();
        SetupSynthesis();

        await _pi.ResearchTopicAsync(Topic, _config);

        // Only MaxLabAgentsPerPI (3) tasks should be dispatched
        await _labAgent.Received(_config.MaxLabAgentsPerPI).ExecuteSearchTaskFullAsync(
            Arg.Any<SearchTask>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResearchTopicAsync_DeduplicatesSourcesByUrl()
    {
        SetupTaskBreakdown(taskCount: 2);

        // Both lab tasks return a source with the same URL
        var sharedUrl = "https://shared.com/article";
        var sourceId = Guid.NewGuid();
        var sharedSource = new Source(sourceId, sharedUrl, "Shared", null, null,
            SourceCredibility.High, "reputable");
        var finding = new Finding("sub", "summary", [], [sourceId], 0.9);
        var result = new LabTaskResult([finding], [sharedSource]);

        _labAgent.ExecuteSearchTaskFullAsync(
                Arg.Any<SearchTask>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(result);

        SetupSynthesis();

        var paper = await _pi.ResearchTopicAsync(Topic, _config);

        // The shared URL should appear only once in bibliography
        paper.Bibliography.Select(s => s.Url).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public async Task ResearchTopicAsync_EmptyLabResults_StillProducesPaper()
    {
        SetupTaskBreakdown(taskCount: 1);
        var emptyResult = new LabTaskResult([], []);
        _labAgent.ExecuteSearchTaskFullAsync(
                Arg.Any<SearchTask>(), Arg.Any<JobConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(emptyResult);
        SetupSynthesis();

        var paper = await _pi.ResearchTopicAsync(Topic, _config);

        paper.Findings.Should().BeEmpty();
        paper.Bibliography.Should().BeEmpty();
        paper.ExecutiveSummary.Should().NotBeNullOrEmpty();
    }
}
