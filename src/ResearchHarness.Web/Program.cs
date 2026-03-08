using System.Threading.Channels;
using Microsoft.Extensions.Options;
using ResearchHarness.Agents;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Infrastructure.Llm;
using ResearchHarness.Infrastructure.Persistence;
using ResearchHarness.Infrastructure.Search;
using ResearchHarness.Orchestration;
using ResearchHarness.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────

// Llm:Provider selects the active backend. Supported values: "Anthropic" (default), "OpenRouter".
var llmProvider = builder.Configuration["Llm:Provider"] ?? "Anthropic";

builder.Services.Configure<BraveSearchOptions>(
    builder.Configuration.GetSection("BraveSearch"));

// JobConfiguration is a record, not a class; bind manually and register as singleton.
var jobConfig = builder.Configuration.GetSection("Research").Get<JobConfiguration>()
    ?? new JobConfiguration();
builder.Services.AddSingleton(jobConfig);

// ── HTTP Clients ───────────────────────────────────────────────────────────

builder.Services.AddHttpClient("Anthropic");
builder.Services.AddHttpClient("OpenRouter", c => c.Timeout = TimeSpan.FromSeconds(300));
builder.Services.AddHttpClient("BraveSearch");
builder.Services.AddHttpClient("BravePageFetcher");

// ── Infrastructure: LLM ───────────────────────────────────────────────────

if (llmProvider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<OpenRouterOptions>(
        builder.Configuration.GetSection("OpenRouter"));
    builder.Services.AddSingleton<RateLimitedExecutor>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
        return new RateLimitedExecutor(opts.MaxConcurrentLlmCalls, maxSearchConcurrency: 5);
    });
    builder.Services.AddSingleton<ILlmClient, OpenRouterLlmClient>();
}
else
{
    builder.Services.Configure<AnthropicOptions>(
        builder.Configuration.GetSection("Anthropic"));
    builder.Services.AddSingleton<RateLimitedExecutor>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        return new RateLimitedExecutor(opts.MaxConcurrentLlmCalls, maxSearchConcurrency: 5);
    });
    builder.Services.AddSingleton<ILlmClient, AnthropicLlmClient>();
}

// ── Infrastructure: Search ─────────────────────────────────────────────────

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SearchResultCache>();
builder.Services.AddSingleton<ISearchProvider, BraveSearchProvider>();
builder.Services.AddSingleton<IPageFetcher, BravePageFetcher>();

// ── Infrastructure: Persistence ───────────────────────────────────────────

builder.Services.AddSingleton<IJobStore, InMemoryJobStore>();

// ── Agents ─────────────────────────────────────────────────────────────────
// LabAgentService implements both ILabAgentService and ILabAgentServiceInternal.
// PrincipalInvestigatorAgent injects ILabAgentServiceInternal.

builder.Services.AddTransient<ILabAgentServiceInternal, LabAgentService>();
builder.Services.AddTransient<ILabAgentService>(
    sp => sp.GetRequiredService<ILabAgentServiceInternal>());

builder.Services.AddTransient<IInstituteLeadAgent, InstituteLeadAgent>();
builder.Services.AddTransient<IPrincipalInvestigatorAgent, PrincipalInvestigatorAgent>();

// ── Orchestration ──────────────────────────────────────────────────────────

// Unbounded channel: job IDs flow from controller → background processor
var jobChannel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
{
    SingleReader = true,  // ResearchJobProcessor is the only reader
    SingleWriter = false  // Multiple concurrent HTTP requests can enqueue
});

builder.Services.AddSingleton(jobChannel);
builder.Services.AddSingleton(jobChannel.Writer);
builder.Services.AddSingleton(jobChannel.Reader);

// ResearchOrchestrator is scoped so it can inject transient agents cleanly.
builder.Services.AddScoped<IResearchOrchestrator, ResearchOrchestrator>();

builder.Services.AddHostedService<ResearchJobProcessor>();

// ── ASP.NET ────────────────────────────────────────────────────────────────

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

// ── Build ──────────────────────────────────────────────────────────────────

var app = builder.Build();

// Phase 1 API key guard for /internal/ routes.
app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

app.Run();
