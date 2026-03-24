using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Agents;

public partial class PeerReviewService : IPeerReviewService
{
    private readonly ILlmClient _llm;
    private readonly ILogger<PeerReviewService> _logger;

    private static readonly ActivitySource ActivitySource =
        new("ResearchHarness.Agents.PeerReview", "1.0.0");

    public PeerReviewService(ILlmClient llm, ILogger<PeerReviewService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<List<ReviewResult>> ReviewPaperAsync(
        Paper paper,
        ResearchTopic topic,
        JobConfiguration config,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("ReviewPaper", ActivityKind.Internal);
        activity?.SetTag("topic.id", topic.TopicId.ToString());
        activity?.SetTag("reviewer.count", config.PeerReviewerCount.ToString());

        var reviewerCount = config.PeerReviewerCount;
        LogReviewStarted(_logger, topic.TopicId, reviewerCount);

        // Dispatch all reviewers in parallel
        var reviewTasks = Enumerable.Range(0, reviewerCount).Select(_ =>
            RunSingleReviewAsync(paper, topic, config, ct));

        var results = await Task.WhenAll(reviewTasks);
        foreach (var result in results)
            LogReviewVerdict(_logger, result.Verdict.ToString(), topic.TopicId);
        return [.. results];
    }

    private async Task<ReviewResult> RunSingleReviewAsync(
        Paper paper,
        ResearchTopic topic,
        JobConfiguration config,
        CancellationToken ct)
    {
        var request = new LlmRequest(
            Model: config.ReviewerModel,
            SystemPrompt: ReviewEvaluationPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(ReviewEvaluationPrompt.BuildUserMessage(paper, topic))],
            OutputSchema: ReviewEvaluationPrompt.BuildOutputSchema()
        );

        var response = await _llm.CompleteAsync<ReviewEvaluationOutput>(request, ct);
        var output = response.Content;

        var verdict = ParseVerdict(output.Verdict);
        return new ReviewResult(
            Verdict: verdict,
            Feedback: output.Feedback ?? "",
            Issues: output.Issues ?? [],
            ReviewedAt: DateTimeOffset.UtcNow
        );
    }

    private static ReviewVerdict ParseVerdict(string? raw)
    {
        return raw?.Trim() switch
        {
            "Accept" => ReviewVerdict.Accept,
            "Reject" => ReviewVerdict.Reject,
            _ => ReviewVerdict.Revise  // unknown defaults to Revise (conservative)
        };
    }

    [LoggerMessage(2004, LogLevel.Information, "Peer reviewing paper for topic {TopicId} with {ReviewerCount} reviewer(s)")]
    private static partial void LogReviewStarted(ILogger logger, Guid topicId, int reviewerCount);

    [LoggerMessage(2010, LogLevel.Information, "Peer review verdict: {Verdict} for topic {TopicId}")]
    private static partial void LogReviewVerdict(ILogger logger, string verdict, Guid topicId);
}
