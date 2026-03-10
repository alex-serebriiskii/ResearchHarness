using Scalar.AspNetCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Threading.RateLimiting;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using System.Threading.Channels;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResearchHarness.Agents;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Infrastructure.Llm;
using ResearchHarness.Infrastructure.Persistence;
using ResearchHarness.Infrastructure.Search;
using ResearchHarness.Infrastructure.Tracking;
using ResearchHarness.Infrastructure.Telemetry;
using ResearchHarness.Orchestration;
using ResearchHarness.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Structured logging ──────────────────────────────────────────────────────
// JSON console logging for production; default console for development.
if (builder.Environment.IsProduction())
{
    builder.Logging.AddJsonConsole(options =>
        options.JsonWriterOptions = new() { Indented = false });
}

// ── Configuration ──────────────────────────────────────────────────────────

// Llm:Provider selects the active backend. Supported values: "Anthropic" (default), "OpenRouter".
var llmProvider = builder.Configuration["Llm:Provider"] ?? "Anthropic";

builder.Services.Configure<BraveSearchOptions>(
    builder.Configuration.GetSection("BraveSearch"));

builder.Services.Configure<RateLimitOptions>(
    builder.Configuration.GetSection("RateLimit"));

// JobConfiguration is a record, not a class; bind manually and register as singleton.
var jobConfig = builder.Configuration.GetSection("Research").Get<JobConfiguration>()
    ?? new JobConfiguration();
builder.Services.AddSingleton(jobConfig);

// ── HTTP Clients ───────────────────────────────────────────────────────────
// Circuit breaker only — no HTTP-level retry (clients handle LLM-specific retry/fallback).
// Breaks after 50% failure rate over 60s with minimum 5 requests; half-open after 30s.

builder.Services.AddHttpClient("Anthropic", client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddResilienceHandler("cb", pipeline =>{
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
        });
    });

builder.Services.AddHttpClient("OpenRouter", client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddResilienceHandler("cb", pipeline =>{
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
        });
    });

builder.Services.AddHttpClient("BraveSearch")
    .AddResilienceHandler("cb", pipeline =>{
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
        });
    });

builder.Services.AddHttpClient("BravePageFetcher");

// ── Infrastructure: LLM ───────────────────────────────────────────────────
// Concrete LLM clients are singletons (stateless, thread-safe HTTP wrappers).
// ILlmClient is scoped via TrackingLlmClient so each job scope gets its own
// token accumulator, while the underlying HTTP client is shared.

if (llmProvider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<OpenRouterOptions>(
        builder.Configuration.GetSection("OpenRouter"));
    builder.Services.AddSingleton<RateLimitedExecutor>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        return new RateLimitedExecutor(opts.LlmConcurrency, opts.SearchConcurrency);
    });
    builder.Services.AddSingleton<OpenRouterLlmClient>();
    builder.Services.AddScoped<ILlmClient>(sp =>
        new TrackingLlmClient(
            sp.GetRequiredService<OpenRouterLlmClient>(),
            sp.GetRequiredService<ITokenTracker>(),
            sp.GetRequiredService<ResearchMetrics>()));
}
else
{
    builder.Services.Configure<AnthropicOptions>(
        builder.Configuration.GetSection("Anthropic"));
    builder.Services.AddSingleton<RateLimitedExecutor>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        return new RateLimitedExecutor(opts.LlmConcurrency, opts.SearchConcurrency);
    });
    builder.Services.AddSingleton<AnthropicLlmClient>();
    builder.Services.AddScoped<ILlmClient>(sp =>
        new TrackingLlmClient(
            sp.GetRequiredService<AnthropicLlmClient>(),
            sp.GetRequiredService<ITokenTracker>(),
            sp.GetRequiredService<ResearchMetrics>()));
}

// Token tracker: one per DI scope = one per job execution.
builder.Services.AddScoped<ITokenTracker, TokenTracker>();

// ── Infrastructure: Search ─────────────────────────────────────────────────

builder.Services.AddMemoryCache();

// ── Observability ──────────────────────────────────────────────────────────
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("ResearchHarness.Orchestration")
            .AddSource("ResearchHarness.Agents.InstituteLeadAgent")
            .AddSource("ResearchHarness.Agents.PrincipalInvestigator")
            .AddSource("ResearchHarness.Agents.LabAgent")
            .AddSource("ResearchHarness.Agents.PeerReview")
            .AddSource("ResearchHarness.Agents.ConsultingFirm")
            .AddConsoleExporter();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(ResearchMetrics.MeterName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

builder.Services.AddSingleton<ResearchMetrics>();

// Register ISearchResultCache: use Redis-backed implementation when configured, in-memory otherwise
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
    builder.Services.AddSingleton<ISearchResultCache, DistributedSearchResultCache>();
}
else
{
    builder.Services.AddSingleton<SearchResultCache>();
    builder.Services.AddSingleton<ISearchResultCache>(sp => sp.GetRequiredService<SearchResultCache>());
}
builder.Services.AddSingleton<ISearchProvider, BraveSearchProvider>();
builder.Services.AddSingleton<IPageFetcher, BravePageFetcher>();

// ── Infrastructure: Persistence ───────────────────────────────────────────

// SQLite persistence: default to %LOCALAPPDATA%\ResearchHarness\jobs.db so the file
// never lands in the repo tree. Override via ConnectionStrings:Jobs in any environment.
var defaultDbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ResearchHarness", "jobs.db");
var jobsConnectionString = builder.Configuration.GetConnectionString("Jobs")
    ?? $"Data Source={defaultDbPath}";
Directory.CreateDirectory(Path.GetDirectoryName(defaultDbPath)!);
builder.Services.AddSingleton<IJobStore>(new SqliteJobStore(jobsConnectionString));

// ── Agents ─────────────────────────────────────────────────────────────────
// LabAgentService implements both ILabAgentService and ILabAgentServiceInternal.
// PrincipalInvestigatorAgent injects ILabAgentServiceInternal.

builder.Services.AddTransient<ILabAgentServiceInternal, LabAgentService>();
builder.Services.AddTransient<ILabAgentService>(
    sp => sp.GetRequiredService<ILabAgentServiceInternal>());

builder.Services.AddTransient<IInstituteLeadAgent, InstituteLeadAgent>();
builder.Services.AddTransient<IPrincipalInvestigatorAgent, PrincipalInvestigatorAgent>();
builder.Services.AddTransient<IPeerReviewService, PeerReviewService>();
builder.Services.AddTransient<IConsultingFirmService, ConsultingFirmService>();

// ── Orchestration ──────────────────────────────────────────────────────────

// Bounded channel: backpressure when more than 100 jobs are queued.
var jobChannel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(100)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,  // ResearchJobProcessor is the only reader
    SingleWriter = false  // Multiple concurrent HTTP requests can enqueue
});

builder.Services.AddSingleton(jobChannel);
builder.Services.AddSingleton(jobChannel.Writer);
builder.Services.AddSingleton(jobChannel.Reader);

// ResearchOrchestrator is scoped so it can inject transient agents and scoped token tracker cleanly.
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();

builder.Services.AddHostedService<ResearchJobProcessor>();

builder.Services.AddSingleton<IJobCancellationService, JobCancellationService>();
builder.Services.Configure<ResearchScheduleOptions>(
    builder.Configuration.GetSection("Research"));
builder.Services.AddHostedService<ScheduledResearchService>();

// ── Rate Limiting ──────────────────────────────────────────────────────────
// Fixed-window: 10 job submissions per minute per API key (or per IP if no key).
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("start-api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Request.Headers["X-Api-Key"].FirstOrDefault()
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health Checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── ASP.NET ────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "ResearchHarness API";
        doc.Info.Version = "v1";
        doc.Info.Description = "Internal API for submitting and monitoring research jobs.";
        return Task.CompletedTask;
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ── Build ──────────────────────────────────────────────────────────────────

var app = builder.Build();

// Phase 1 API key guard for /internal/ routes.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseRateLimiter();

app.MapHealthChecks("/health");

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "ResearchHarness";
        options.DefaultHttpClient = new(ScalarTarget.Shell, ScalarClient.Curl);
    });
}

app.MapControllers();

app.Run();
