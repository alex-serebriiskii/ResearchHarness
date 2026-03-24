using ResearchHarness.Agents.Security;
using System.Diagnostics;
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

public partial class LabAgentService : ILabAgentServiceInternal
{
    private readonly ILlmClient _llm;
    private readonly ISearchProvider _search;
    private readonly IPageFetcher _pageFetcher;
    private readonly ILogger<LabAgentService> _logger;

    private static readonly ActivitySource ActivitySource =
        new("ResearchHarness.Agents.LabAgent", "1.0.0");

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
        using var activity = ActivitySource.StartActivity("ExecuteSearchTask", ActivityKind.Internal);
        activity?.SetTag("query", task.Query);
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["SearchQuery"] = task.Query
        }))
        {
        // Step 1: Search
        var options = new SearchOptions(Count: config.SearchResultsPerQuery);
        var searchResults = await _search.SearchAsync(task.Query, options, ct);
        LogSearchHits(_logger, searchResults.Hits.Count, task.Query);

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

        foreach (var s in extractResponse.Content.Sources ?? [])
        {
            if (string.IsNullOrWhiteSpace(s.Url))
                continue; // Cannot register a source without a URL
            var url = s.Url!;
            if (!PromptSanitizer.IsAllowedUrl(url))
                continue; // Reject non-http(s) URLs
            var id = Guid.NewGuid();
            var cred = Enum.TryParse<SourceCredibility>(s.Credibility, out var parsed)
                ? parsed
                : SourceCredibility.Unknown;
            sourceById[id] = new Source(id, url, s.Title ?? url, s.Author, null, cred, s.CredibilityRationale ?? "");
            sourceByUrl[url] = id;
        }

        var findings = (extractResponse.Content.Findings ?? []).Select(f =>
        {
            Guid sourceId;
            var sourceUrl = f.SourceUrl;
            if (string.IsNullOrWhiteSpace(sourceUrl) || !sourceByUrl.TryGetValue(sourceUrl!, out sourceId))
            {
                // Null/empty URL or URL not in sources list — create a fallback entry
                sourceId = Guid.NewGuid();
                var fallbackUrl = string.IsNullOrWhiteSpace(sourceUrl) ? $"unknown:{sourceId}" : sourceUrl!;
                // Validate non-synthetic fallback URLs
                if (!fallbackUrl.StartsWith("unknown:") && !PromptSanitizer.IsAllowedUrl(fallbackUrl))
                    fallbackUrl = $"unknown:{sourceId}";
                var fallback = new Source(
                    sourceId, fallbackUrl, fallbackUrl,
                    null, null, SourceCredibility.Unknown, "Not assessed");
                sourceById[sourceId] = fallback;
                if (!string.IsNullOrWhiteSpace(sourceUrl) && fallbackUrl == sourceUrl)
                    sourceByUrl[sourceUrl!] = sourceId;
            }

            return new Finding(f.SubTopic ?? "", f.Summary ?? "", f.KeyPoints ?? [], [sourceId], f.RelevanceScore);
        }).ToList();

        // Cross-reference: warn about findings citing URLs not in original search results
        var knownUrls = new HashSet<string>(
            searchResults.Hits.Select(h => h.Url),
            StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceById.Values)
        {
            if (!knownUrls.Contains(source.Url) && !source.Url.StartsWith("unknown:"))
                LogUnknownSourceUrl(_logger, source.Url, task.Query);
        }

        LogExtractionResults(_logger, findings.Count, sourceById.Count, task.Query);

        return new LabTaskResult(findings, [.. sourceById.Values]);
        }
    }

    [LoggerMessage(2003, LogLevel.Debug, "Lab got {Count} hits for query: {Query}")]
    private static partial void LogSearchHits(ILogger logger, int count, string query);

    [LoggerMessage(2006, LogLevel.Warning, "Extracted source URL {Url} not found in search results for query: {Query}")]
    private static partial void LogUnknownSourceUrl(ILogger logger, string url, string query);

    [LoggerMessage(2007, LogLevel.Information, "Lab extracted {FindingCount} findings and {SourceCount} sources for query: {Query}")]
    private static partial void LogExtractionResults(ILogger logger, int findingCount, int sourceCount, string query);
}
