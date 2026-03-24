using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;
using ResearchHarness.Infrastructure.Telemetry;

namespace ResearchHarness.Infrastructure.Search;

public sealed partial class BravePageFetcher : IPageFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BraveSearchOptions _options;
    private readonly ResearchMetrics _metrics;
    private readonly ILogger<BravePageFetcher> _logger;

    public BravePageFetcher(
        IHttpClientFactory httpClientFactory,
        IOptions<BraveSearchOptions> options,
        ResearchMetrics metrics,
        ILogger<BravePageFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<PageContent> FetchAsync(string url, CancellationToken ct = default)
    {
        LogPageFetchAttempt(_logger, url);
        using var client = _httpClientFactory.CreateClient("BravePageFetcher");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.PageFetchTimeoutSeconds));

        string html;
        try
        {
            using var response = await client.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Page fetch returned {StatusCode} for {Url}",
                    response.StatusCode, url);
                _metrics.RecordPageFailed();
                return new PageContent(url, "", null, null);
            }

            html = await response.Content.ReadAsStringAsync(cts.Token);
            LogPageFetchSuccess(_logger, url, html.Length);
            _metrics.RecordPageFetched();
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Page fetch failed for {Url}", url);
            _metrics.RecordPageFailed();
            return new PageContent(url, "", null, null);
        }

        var title = TryExtractTitle(html);
        var text = StripHtml(html);
        return new PageContent(url, text, title, null);
    }

    private static string? TryExtractTitle(string html)
    {
        var match = Regex.Match(
            html,
            @"<title>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string StripHtml(string html)
    {
        var text = Regex.Replace(
            html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");

        text = text
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length > 8000 ? text[..8000] : text;
    }

    [LoggerMessage(4002, LogLevel.Information, "Fetching page: {Url}")]
    private static partial void LogPageFetchAttempt(ILogger logger, string url);

    [LoggerMessage(4003, LogLevel.Information, "Page fetched successfully: {Url} ({ContentLength} bytes)")]
    private static partial void LogPageFetchSuccess(ILogger logger, string url, int contentLength);
}
