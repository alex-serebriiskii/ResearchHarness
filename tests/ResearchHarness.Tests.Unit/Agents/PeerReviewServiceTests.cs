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

public class PeerReviewServiceTests
{
    private ILlmClient _llm = null!;
    private PeerReviewService _service = null!;

    private static readonly ResearchTopic SampleTopic = new(
        TopicId: Guid.NewGuid(),
        Title: "AI Safety",
        Scope: "Safety in AI systems",
        SuggestedSearchAngles: [],
        ExpectedSourceTypes: [],
        Status: TopicStatus.Pending,
        Paper: null
    );

    private static Paper BuildPaper() => new(
        TopicId: SampleTopic.TopicId,
        ExecutiveSummary: "AI safety research summary",
        Findings: [],
        Bibliography: [],
        ConfidenceScore: 0.8,
        RevisionCount: 0,
        Reviews: []
    );

    [Before(Test)]
    public void Setup()
    {
        _llm = Substitute.For<ILlmClient>();
        _service = new PeerReviewService(_llm, Substitute.For<ILogger<PeerReviewService>>());
    }

    private void SetupReviewerResponse(string verdict, string feedback = "Good paper", List<string>? issues = null)
    {
        var output = new ReviewEvaluationOutput(verdict, feedback, issues ?? []);
        _llm.CompleteAsync<ReviewEvaluationOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<ReviewEvaluationOutput>(output, new TokenUsage(100, 50), "tool_use"));
    }

    [Test]
    public async Task ReviewPaperAsync_HappyPath_ReturnsReviews()
    {
        var config = new JobConfiguration(PeerReviewerCount: 2, ReviewerModel: "claude-test");
        SetupReviewerResponse("Accept");

        var results = await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Verdict.Should().Be(ReviewVerdict.Accept));
    }

    [Test]
    public async Task ReviewPaperAsync_ReviewerCount_MatchesConfig()
    {
        var config = new JobConfiguration(PeerReviewerCount: 3, ReviewerModel: "claude-test");
        SetupReviewerResponse("Revise");

        var results = await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        results.Should().HaveCount(3);
        await _llm.Received(3).CompleteAsync<ReviewEvaluationOutput>(
            Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReviewPaperAsync_AcceptVerdict_Parsed()
    {
        var config = new JobConfiguration(PeerReviewerCount: 1, ReviewerModel: "claude-test");
        SetupReviewerResponse("Accept", "Excellent work", []);

        var results = await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        results[0].Verdict.Should().Be(ReviewVerdict.Accept);
        results[0].Feedback.Should().Be("Excellent work");
    }

    [Test]
    public async Task ReviewPaperAsync_RejectVerdict_Parsed()
    {
        var config = new JobConfiguration(PeerReviewerCount: 1, ReviewerModel: "claude-test");
        SetupReviewerResponse("Reject", "Fundamental flaws", ["No sources"]);

        var results = await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        results[0].Verdict.Should().Be(ReviewVerdict.Reject);
        results[0].Issues.Should().HaveCount(1);
    }

    [Test]
    public async Task ReviewPaperAsync_UnknownVerdict_DefaultsToRevise()
    {
        var config = new JobConfiguration(PeerReviewerCount: 1, ReviewerModel: "claude-test");
        SetupReviewerResponse("UNKNOWN_VERDICT");

        var results = await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        results[0].Verdict.Should().Be(ReviewVerdict.Revise);
    }

    [Test]
    public async Task ReviewPaperAsync_UsesReviewerModel()
    {
        var config = new JobConfiguration(PeerReviewerCount: 1, ReviewerModel: "test-reviewer-model");
        SetupReviewerResponse("Accept");

        await _service.ReviewPaperAsync(BuildPaper(), SampleTopic, config);

        await _llm.Received(1).CompleteAsync<ReviewEvaluationOutput>(
            Arg.Is<LlmRequest>(r => r.Model == "test-reviewer-model"),
            Arg.Any<CancellationToken>());
    }
}
