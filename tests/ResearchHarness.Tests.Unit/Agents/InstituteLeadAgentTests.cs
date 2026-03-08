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

public class InstituteLeadAgentTests
{
    private ILlmClient _llm = null!;
    private InstituteLeadAgent _agent = null!;
    private JobConfiguration _config = null!;

    [Before(Test)]
    public void Setup()
    {
        _llm = Substitute.For<ILlmClient>();
        _config = new JobConfiguration(MaxTopics: 1, LeadModel: "claude-test");
        _agent = new InstituteLeadAgent(
            _llm,
            _config,
            Substitute.For<ILogger<InstituteLeadAgent>>()
        );
    }

    [Test]
    public async Task DecomposeThemeAsync_ReturnsTopics_MappedFromLlmOutput()
    {
        var dto = new TopicDecompositionOutput(
        [
            new TopicDto(
                "AI Safety",
                "Scope of AI safety",
                ["angle1", "angle2"],
                ["academic", "news"])
        ]);

        _llm.CompleteAsync<TopicDecompositionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<TopicDecompositionOutput>(dto, new TokenUsage(100, 200), "tool_use"));

        var topics = await _agent.DecomposeThemeAsync("AI Safety 2025", _config);

        topics.Should().HaveCount(1);
        topics[0].Title.Should().Be("AI Safety");
        topics[0].Scope.Should().Be("Scope of AI safety");
        topics[0].SuggestedSearchAngles.Should().HaveCount(2);
        topics[0].Status.Should().Be(TopicStatus.Pending);
        topics[0].Paper.Should().BeNull();
        topics[0].TopicId.Should().NotBeEmpty();
    }

    [Test]
    public async Task DecomposeThemeAsync_Phase1Cap_LimitsToOneTopic()
    {
        // LLM returns 3 topics but Phase 1 caps at 1
        var dto = new TopicDecompositionOutput(
        [
            new TopicDto("Topic 1", "Scope 1", [], []),
            new TopicDto("Topic 2", "Scope 2", [], []),
            new TopicDto("Topic 3", "Scope 3", [], [])
        ]);

        _llm.CompleteAsync<TopicDecompositionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<TopicDecompositionOutput>(dto, new TokenUsage(50, 100), "tool_use"));

        var topics = await _agent.DecomposeThemeAsync("broad theme", _config);

        topics.Should().HaveCount(1);
        topics[0].Title.Should().Be("Topic 1");
    }

    [Test]
    public async Task DecomposeThemeAsync_UsesLeadModel()
    {
        var dto = new TopicDecompositionOutput([new TopicDto("T", "S", [], [])]);
        _llm.CompleteAsync<TopicDecompositionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<TopicDecompositionOutput>(dto, new TokenUsage(10, 20), "tool_use"));

        await _agent.DecomposeThemeAsync("theme", _config);

        await _llm.Received(1).CompleteAsync<TopicDecompositionOutput>(
            Arg.Is<LlmRequest>(r => r.Model == _config.LeadModel),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DecomposeThemeAsync_IncludesThemeInUserMessage()
    {
        var dto = new TopicDecompositionOutput([new TopicDto("T", "S", [], [])]);
        _llm.CompleteAsync<TopicDecompositionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<TopicDecompositionOutput>(dto, new TokenUsage(10, 20), "tool_use"));

        const string theme = "cardiovascular drug pipeline 2025";
        await _agent.DecomposeThemeAsync(theme, _config);

        await _llm.Received(1).CompleteAsync<TopicDecompositionOutput>(
            Arg.Is<LlmRequest>(r => r.Messages.Any(m => m.Content.Contains(theme))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AssembleJournalAsync_ReturnsDedupedMasterBibliography()
    {
        var sharedUrl = "https://example.com/shared";
        var sourceA = new Source(Guid.NewGuid(), sharedUrl, "Shared Source", null, null,
            SourceCredibility.High, "reputable");
        var sourceB = new Source(Guid.NewGuid(), sharedUrl, "Duplicate", null, null,
            SourceCredibility.High, "reputable");
        var sourceC = new Source(Guid.NewGuid(), "https://other.com", "Other", null, null,
            SourceCredibility.Medium, "ok");

        var paper1 = new Paper(Guid.NewGuid(), "Summary 1", [], [sourceA], 0.9, 0, []);
        var paper2 = new Paper(Guid.NewGuid(), "Summary 2", [], [sourceB, sourceC], 0.8, 0, []);

        var journalOutput = new JournalAssemblyOutput("overall summary", "cross analysis");
        _llm.CompleteAsync<JournalAssemblyOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<JournalAssemblyOutput>(journalOutput, new TokenUsage(100, 200), "tool_use"));

        var journal = await _agent.AssembleJournalAsync("theme", [paper1, paper2]);

        journal.OverallSummary.Should().Be("overall summary");
        journal.CrossTopicAnalysis.Should().Be("cross analysis");
        // Deduplication: sourceA and sourceB share URL — only first should appear
        journal.MasterBibliography.Should().HaveCount(2);
        journal.MasterBibliography.Select(s => s.Url).Should().OnlyHaveUniqueItems();
    }
}
