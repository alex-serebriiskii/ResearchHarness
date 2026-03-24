using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Agents;

public partial class InstituteLeadAgent : IInstituteLeadAgent
{
    private readonly ILlmClient _llm;
    private readonly JobConfiguration _config;
    private readonly ILogger<InstituteLeadAgent> _logger;

    private static readonly ActivitySource ActivitySource =
        new("ResearchHarness.Agents.InstituteLeadAgent", "1.0.0");

    public InstituteLeadAgent(
        ILlmClient llm,
        JobConfiguration config,
        ILogger<InstituteLeadAgent> logger)
    {
        _llm = llm;
        _config = config;
        _logger = logger;
    }

    public async Task<List<ResearchTopic>> DecomposeThemeAsync(
        string theme,
        JobConfiguration config,
        string? domainContext = null,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("DecomposeTheme", ActivityKind.Internal);
        activity?.SetTag("model", config.LeadModel);

        int topicsToRequest = config.MaxTopics;

        var request = new LlmRequest(
            Model: config.LeadModel,
            SystemPrompt: LeadDecompositionPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(LeadDecompositionPrompt.BuildUserMessage(theme, topicsToRequest, domainContext))],
            OutputSchema: LeadDecompositionPrompt.BuildOutputSchema()
        );

        var response = await _llm.CompleteAsync<TopicDecompositionOutput>(request, ct);
        LogThemeDecomposed(_logger, response.Content.Topics?.Count ?? 0);

        var topics = (response.Content.Topics ?? [])
            .Take(topicsToRequest)
            .Select(t => new ResearchTopic(
                TopicId: Guid.NewGuid(),
                Title: t.Title,
                Scope: t.Scope,
                SuggestedSearchAngles: t.SuggestedSearchAngles ?? [],
                ExpectedSourceTypes: t.ExpectedSourceTypes ?? [],
                Status: TopicStatus.Pending,
                Paper: null))
            .ToList();

        foreach (var topic in topics)
            LogTopicTitle(_logger, topic.Title);

        return topics;
    }

    public async Task<Journal> AssembleJournalAsync(
        string theme,
        List<Paper> papers,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("AssembleJournal", ActivityKind.Internal);
        activity?.SetTag("model", _config.LeadModel);
        activity?.SetTag("paper.count", papers.Count.ToString());

        var config = _config;

        var request = new LlmRequest(
            Model: config.LeadModel,
            SystemPrompt: JournalAssemblyPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(JournalAssemblyPrompt.BuildUserMessage(theme, papers))],
            OutputSchema: JournalAssemblyPrompt.BuildOutputSchema()
        );

        var response = await _llm.CompleteAsync<JournalAssemblyOutput>(request, ct);

        // Deduplicate sources from all papers by URL, keeping the first occurrence
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dedupedSources = papers
            .SelectMany(p => p.Bibliography)
            .Where(s => seenUrls.Add(s.Url))
            .ToList();

        return new Journal(
            OverallSummary: response.Content.OverallSummary ?? "",
            CrossTopicAnalysis: response.Content.CrossTopicAnalysis ?? "",
            Papers: papers,
            MasterBibliography: dedupedSources,
            AssembledAt: DateTimeOffset.UtcNow);
    }

    [LoggerMessage(2001, LogLevel.Information, "Lead decomposed theme into {Count} topics")]
    private static partial void LogThemeDecomposed(ILogger logger, int count);

    [LoggerMessage(2009, LogLevel.Information, "Lead decomposed topic: {TopicTitle}")]
    private static partial void LogTopicTitle(ILogger logger, string topicTitle);
}
