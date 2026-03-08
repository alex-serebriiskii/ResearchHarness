using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Agents;

/// <summary>
/// Compound result returned by the lab agent: extracted findings paired with
/// the sources they reference. The PI uses this to build the paper bibliography.
/// </summary>
public record LabTaskResult(List<Finding> Findings, List<Source> Sources);

/// <summary>
/// Extends ILabAgentService with a richer return type so the PI can collect
/// source objects without a side-channel registry.
/// </summary>
public interface ILabAgentServiceInternal : ILabAgentService
{
    Task<LabTaskResult> ExecuteSearchTaskFullAsync(
        SearchTask task,
        JobConfiguration config,
        CancellationToken ct = default);
}

public class LabAgentService : ILabAgentServiceInternal
{
    private readonly ILlmClient _llm;
    private readonly ISearchProvider _search;
    private readonly IPageFetcher _pageFetcher;
    private readonly ILogger<LabAgentService> _logger;

    public LabAgentService(
        ILlmClient llm,
        ISearchProvider search,
        IPageFetcher pageFetcher,
        ILogger<LabAgentService> logger)
    {
        _llm = llm;
        _search = search;
        _pageFetcher = pageFetcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<Finding>> ExecuteSearchTaskAsync(
        SearchTask task,
        JobConfiguration config,
        CancellationToken ct = default)
        => (await ExecuteSearchTaskFullAsync(task, config, ct)).Findings;

    /// <inheritdoc />
    public async Task<LabTaskResult> ExecuteSearchTaskFullAsync(
        SearchTask task,
        JobConfiguration config,
        CancellationToken ct = default)
    {
        // Step 1: Search
        var options = new SearchOptions(Count: config.SearchResultsPerQuery);
        var searchResults = await _search.SearchAsync(task.Query, options, ct);
        _logger.LogDebug("Lab got {Count} hits for query: {Query}", searchResults.Hits.Count, task.Query);

        // Step 2: Optionally fetch full page content (capped at 3 fetches to limit latency)
        var pages = new List<PageContent>();
        if (task.FetchPageContent)
        {
            foreach (var hit in searchResults.Hits.Take(3))
            {
                var page = await _pageFetcher.FetchAsync(hit.Url, ct);
                if (!string.IsNullOrWhiteSpace(page.RawText))
                    pages.Add(page);
            }
        }

        // Step 3: LLM extraction
        var extractRequest = new LlmRequest(
            Model: config.LabModel,
            SystemPrompt: LabExtractionPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(LabExtractionPrompt.BuildUserMessage(task, searchResults.Hits, pages))],
            OutputSchema: LabExtractionPrompt.BuildOutputSchema()
        );
        var extractResponse = await _llm.CompleteAsync<LabExtractionOutput>(extractRequest, ct);

        // Step 4: Map to domain types, minting stable SourceIds
        var sourceById = new Dictionary<Guid, Source>();
        var sourceByUrl = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in extractResponse.Content.Sources)
        {
            var id = Guid.NewGuid();
            var cred = Enum.TryParse<SourceCredibility>(s.Credibility, out var parsed)
                ? parsed
                : SourceCredibility.Unknown;
            sourceById[id] = new Source(id, s.Url, s.Title, s.Author, null, cred, s.CredibilityRationale);
            sourceByUrl[s.Url] = id;
        }

        var findings = extractResponse.Content.Findings.Select(f =>
        {
            if (!sourceByUrl.TryGetValue(f.SourceUrl, out var sourceId))
            {
                // Source referenced by a finding but absent from the sources list — create a fallback entry
                sourceId = Guid.NewGuid();
                var fallback = new Source(
                    sourceId, f.SourceUrl, f.SourceUrl,
                    null, null, SourceCredibility.Unknown, "Not assessed");
                sourceById[sourceId] = fallback;
                sourceByUrl[f.SourceUrl] = sourceId;
            }

            return new Finding(f.SubTopic, f.Summary, f.KeyPoints, [sourceId], f.RelevanceScore);
        }).ToList();

        return new LabTaskResult(findings, [.. sourceById.Values]);
    }
}
