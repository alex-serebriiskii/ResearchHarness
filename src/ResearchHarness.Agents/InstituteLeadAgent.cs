using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Agents;

public class InstituteLeadAgent : IInstituteLeadAgent
{
    private readonly ILlmClient _llm;
    private readonly JobConfiguration _config;
    private readonly ILogger<InstituteLeadAgent> _logger;

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
        CancellationToken ct = default)
    {
        int topicsToRequest = Math.Min(config.MaxTopics, 1); // Phase 1: cap at 1

        var request = new LlmRequest(
            Model: config.LeadModel,
            SystemPrompt: LeadDecompositionPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(LeadDecompositionPrompt.BuildUserMessage(theme, topicsToRequest))],
            OutputSchema: LeadDecompositionPrompt.BuildOutputSchema()
        );

        var response = await _llm.CompleteAsync<TopicDecompositionOutput>(request, ct);
        _logger.LogInformation("Lead decomposed theme into {Count} topics", response.Content.Topics?.Count ?? 0);

        return (response.Content.Topics ?? [])
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
    }

    public async Task<Journal> AssembleJournalAsync(
        string theme,
        List<Paper> papers,
        CancellationToken ct = default)
    {
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
}
