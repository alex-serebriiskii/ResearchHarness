using System.Diagnostics.Metrics;

namespace ResearchHarness.Infrastructure.Telemetry;

/// <summary>
/// OpenTelemetry metrics instruments for the research pipeline.
/// Singleton — all recording methods are thread-safe.
/// </summary>
public sealed class ResearchMetrics
{
    public const string MeterName = "ResearchHarness";

    private readonly Counter<long> _jobsStarted;
    private readonly Counter<long> _jobsCompleted;
    private readonly Counter<long> _jobsFailed;
    private readonly Histogram<double> _jobDurationSeconds;
    private readonly Counter<long> _llmCalls;
    private readonly Counter<long> _llmTokensInput;
    private readonly Counter<long> _llmTokensOutput;
    private readonly Counter<long> _searchCacheHits;
    private readonly Counter<long> _searchCacheMisses;
    private readonly Counter<long> _searchQueries;
    private readonly Counter<long> _pagesFetched;
    private readonly Counter<long> _pagesFailed;

    public ResearchMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _jobsStarted = meter.CreateCounter<long>("research.jobs.started", "jobs", "Total research jobs started");
        _jobsCompleted = meter.CreateCounter<long>("research.jobs.completed", "jobs", "Total research jobs completed");
        _jobsFailed = meter.CreateCounter<long>("research.jobs.failed", "jobs", "Total research jobs failed");
        _jobDurationSeconds = meter.CreateHistogram<double>("research.job.duration", "s", "Research job end-to-end duration");
        _llmCalls = meter.CreateCounter<long>("research.llm.calls", "calls", "Total LLM API calls");
        _llmTokensInput = meter.CreateCounter<long>("research.llm.tokens.input", "tokens", "Total LLM input tokens consumed");
        _llmTokensOutput = meter.CreateCounter<long>("research.llm.tokens.output", "tokens", "Total LLM output tokens generated");
        _searchCacheHits = meter.CreateCounter<long>("research.search.cache.hits", "hits", "Search cache hits");
        _searchCacheMisses = meter.CreateCounter<long>("research.search.cache.misses", "misses", "Search cache misses");
        _searchQueries = meter.CreateCounter<long>("research.search.queries", "queries", "Total search queries executed");
        _pagesFetched = meter.CreateCounter<long>("research.search.pages.fetched", "pages", "Total pages fetched successfully");
        _pagesFailed = meter.CreateCounter<long>("research.search.pages.failed", "pages", "Total page fetch failures");
    }

    public void RecordJobStarted() => _jobsStarted.Add(1);
    public void RecordJobCompleted(double durationSeconds)
    {
        _jobsCompleted.Add(1);
        _jobDurationSeconds.Record(durationSeconds);
    }
    public void RecordJobFailed() => _jobsFailed.Add(1);
    public void RecordLlmCall(int inputTokens, int outputTokens)
    {
        _llmCalls.Add(1);
        _llmTokensInput.Add(inputTokens);
        _llmTokensOutput.Add(outputTokens);
    }
    public void RecordSearchCacheHit() => _searchCacheHits.Add(1);
    public void RecordSearchCacheMiss() => _searchCacheMisses.Add(1);
    public void RecordSearchQuery() => _searchQueries.Add(1);
    public void RecordPageFetched() => _pagesFetched.Add(1);
    public void RecordPageFailed() => _pagesFailed.Add(1);
}
