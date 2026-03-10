---
name: researchharness
description: Architecture, development workflow, and debugging guide for the ResearchHarness .NET 10 agentic research pipeline
alwaysApply: false
---

# Repository Guidelines

## Project Overview

ResearchHarness is a .NET 10 multi-agent research pipeline. Given a user-provided theme, it decomposes it into research topics, dispatches AI agents to search the web and extract structured findings, synthesizes papers per topic with peer review cycles, and assembles a final journal with cross-topic analysis and master bibliography.

The pipeline is a **directed workflow** (not conversational agents):
1. **InstituteLeadAgent** decomposes theme into ResearchTopics
2. **PrincipalInvestigatorAgent** breaks each topic into SearchTasks, dispatches lab agents, synthesizes a Paper
3. **LabAgentService** executes search (Brave Search) + optional page fetch + LLM extraction per task
4. **PeerReviewService** reviews papers and returns Accept/Revise/Reject verdicts
5. **ConsultingFirmService** provides optional domain briefings before research begins
6. **InstituteLeadAgent** assembles all accepted papers into a final Journal

External services: **OpenRouter** (LLM gateway to Anthropic, OpenAI, Meta models) and **Brave Search** (web search + page fetching). LLM structured output uses tool-use blocks with JSON schemas, not regex parsing.

## Architecture

### Layer Dependency Graph

```
Web --> Orchestration --> Agents --> Core
             |               |
         Infrastructure --> Core
```

All interfaces live in `Core`. Implementations are in `Agents` (agent logic), `Infrastructure` (HTTP clients, persistence, caching, telemetry), and `Orchestration` (pipeline coordination, background processing). `Web` is the ASP.NET Core host and composition root.

### Project Responsibilities

| Project | Purpose |
|---|---|
| `ResearchHarness.Core` | Domain models (all `record` types), interfaces, LLM request/response types, `JobConfiguration`. Zero NuGet dependencies. |
| `ResearchHarness.Agents` | Agent implementations, prompt factories (`Prompts/`), internal LLM DTOs (`Internal/AgentDtos.cs`), `AgentSerializerOptions`. |
| `ResearchHarness.Infrastructure` | `OpenRouterLlmClient`, `AnthropicLlmClient`, `BraveSearchProvider`, `BravePageFetcher`, `SqliteJobStore`, `TokenTracker`, `TrackingLlmClient` (decorator), `RateLimitedExecutor`, `ResearchMetrics`. |
| `ResearchHarness.Orchestration` | `ResearchOrchestrator` (pipeline), `ResearchJobProcessor` (BackgroundService draining `Channel<Guid>`), `ScheduledResearchService` (cron via NCrontab), `JobCancellationService`. |
| `ResearchHarness.Web` | `Program.cs` (DI composition root), `ResearchController`, `ApiKeyMiddleware`. |

### Data Flow

```
HTTP POST /internal/research/start
  --> ResearchController
  --> IResearchOrchestrator.StartResearchAsync (saves job to SQLite, writes Guid to Channel<Guid>)
  --> ResearchJobProcessor (BackgroundService) reads from channel, creates DI scope
  --> RunJobAsync pipeline:
      1. Optional: ConsultingFirmService domain briefing
      2. InstituteLeadAgent.DecomposeThemeAsync --> List<ResearchTopic>
      3. Parallel Task.WhenAll: one PrincipalInvestigatorAgent per topic
         a. PI breaks topic into SearchTasks via LLM
         b. Lab agents execute concurrently (search --> fetch --> LLM extraction)
         c. PI synthesizes findings into Paper
      4. Per paper: PeerReviewService review cycle (up to MaxRevisionsPerPaper rounds)
      5. InstituteLeadAgent.AssembleJournalAsync --> Journal
  --> Job updated with Journal + CostSummary, marked Completed
  --> Client polls GET /internal/research/{id}/status
  --> Client retrieves GET /internal/research/{id}/journal
```

## Key Directories

```
src/
  ResearchHarness.Core/
    Configuration/          # JobConfiguration record
    Interfaces/             # All service interfaces (ILlmClient, IJobStore, etc.)
    Llm/                    # LlmRequest, LlmResponse, TokenUsage
    Models/                 # Domain records: ResearchJob, Paper, Journal, Finding, Source, etc.
  ResearchHarness.Agents/
    Internal/               # AgentDtos.cs — LLM response DTOs (internal visibility)
    Prompts/                # Static prompt factory classes per agent role
  ResearchHarness.Infrastructure/
    Llm/                    # OpenRouterLlmClient, AnthropicLlmClient, LlmJsonRepair, options, exceptions
    Persistence/            # SqliteJobStore
    Search/                 # BraveSearchProvider, BravePageFetcher, search result caching
    Tracking/               # TokenTracker, TrackingLlmClient (decorator)
    Telemetry/              # ResearchMetrics (OpenTelemetry counters/histograms)
  ResearchHarness.Orchestration/   # ResearchOrchestrator, ResearchJobProcessor, ScheduledResearchService
  ResearchHarness.Web/
    Controllers/            # ResearchController
    Properties/             # launchSettings.json (port 5000, Development env)
tests/
  ResearchHarness.Tests.Unit/
    Agents/                 # Per-agent test files + AgentDtoTests
    Infrastructure/         # LLM client, job store, JSON repair, token tracking tests
    Orchestration/          # ResearchOrchestratorTests (core pipeline tests)
  ResearchHarness.Tests.Integration/  # WebApplicationFactory tests, MockLlmClient, model compat harness
tools/                      # PowerShell scripts (build, test, start, stop, submit, poll, show-journal)
skills/researchharness/     # OMP skill pack (SKILL.md, debugging.md, api-reference.md)
```

## Development Commands

**.NET SDK 10.0.103** is required (`global.json` pins it with `rollForward: latestMinor`).

All development commands use PowerShell scripts in `tools/`. On Windows, the running process locks DLLs, so always stop before building.

```powershell
# Stop the running app (required before build on Windows due to DLL locking)
powershell -File tools\stop-app.ps1

# Build
powershell -File tools\build.ps1                       # Debug config
powershell -File tools\build.ps1 -Configuration Release
powershell -File tools\build.ps1 -StopApp              # Kills process first, then builds

# Run unit tests (TUnit on Microsoft.Testing.Platform)
powershell -File tools\run-tests.ps1
powershell -File tools\run-tests.ps1 -NoBuild           # Skip build step

# Run integration tests (not covered by run-tests.ps1)
dotnet test tests\ResearchHarness.Tests.Integration\ResearchHarness.Tests.Integration.csproj

# Start the web app (builds, starts background process on port 5000, polls health for 30s)
powershell -File tools\start-app.ps1
powershell -File tools\start-app.ps1 -Port 5001

# Submit a research job
powershell -File tools\submit-job.ps1 -Theme "your research query"

# Poll job status (10s interval, 30min timeout)
powershell -File tools\poll-job.ps1 -JobId {guid}

# Retrieve completed journal
powershell -File tools\show-journal.ps1 -JobId {guid}
```

**TUnit CLI caveat:** Do NOT pass `--nologo` to `dotnet test` — TUnit on Microsoft.Testing.Platform does not support that flag.

**Typical workflow:** stop --> build --> test --> start --> submit --> poll --> show-journal.

## Code Conventions

### Language and Framework

- **C# on .NET 10** (net10.0 target for all projects)
- Nullable reference types enabled everywhere
- `TreatWarningsAsErrors` enabled in all projects
- Implicit usings enabled
- File-scoped namespaces (`namespace X;` not `namespace X { }`)
- No `.editorconfig` — formatting conventions are implicit

### Naming

- **PascalCase** for all public types, methods, properties
- Domain models are **positional `record` types** (not classes)
- Interfaces prefixed with `I` (standard C#: `ILlmClient`, `IJobStore`)
- Internal DTOs for LLM responses use `internal record` in `Agents/Internal/AgentDtos.cs`
- Prompt factory classes are static with methods: `BuildSystemPrompt()`, `BuildUserMessage()`, `BuildOutputSchema()`
- Test naming: `MethodName_Scenario_ExpectedBehavior`

### JSON Serialization (Critical)

**This is the most dangerous area of the codebase. Violations cause silent data loss, not exceptions.**

- LLM APIs use **snake_case**; C# uses **PascalCase**
- The bridge is `JsonNamingPolicy.SnakeCaseLower` + `PropertyNameCaseInsensitive = true`
- Centralized in `AgentSerializerOptions.Default`:
  ```csharp
  internal static readonly JsonSerializerOptions Default = new()
  {
      PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      PropertyNameCaseInsensitive = true
  };
  ```
- **Every** LLM DTO string field MUST be `string?` (nullable)
- **Every** LLM DTO list field MUST be `List<T>?` (nullable)
- At mapping sites: coalesce with `?? []` for lists, `?? ""` for strings
- Without `SnakeCaseLower`, every snake_case field silently deserializes as `null` — no exception thrown

### Dependency Injection

- `Program.cs` is the single composition root
- LLM provider selection is config-driven (`Llm:Provider` → `"OpenRouter"` or `"Anthropic"`)
- `ILlmClient` is registered **scoped** (wrapped with `TrackingLlmClient` decorator per DI scope for per-job token tracking)
- Infrastructure singletons: `OpenRouterLlmClient`, `AnthropicLlmClient`, `RateLimitedExecutor`
- Agent services resolved per-request via `IServiceProvider.GetRequiredService<T>()` inside the orchestrator
- `IOptions<T>` pattern for configuration sections (`OpenRouterOptions`, `AnthropicOptions`, `BraveSearchOptions`, etc.)
- Named `HttpClient` instances with circuit breakers via `Microsoft.Extensions.Http.Resilience`

### Error Handling

- `LlmException` carries `StatusCode` and `RawResponse` for diagnostics
- LLM clients retry with exponential backoff on 429 (rate limit) and 5xx (server errors); 400s are not retried
- `RateLimitedExecutor` uses separate `SemaphoreSlim` instances for LLM and search concurrency
- Pipeline tolerates partial topic failures: individual PI exceptions are caught, logged, and the job continues with successful topics
- `OperationCanceledException` is always re-thrown (never swallowed)
- `LlmJsonRepair` attempts to fix malformed LLM JSON output before deserialization

### Async Patterns

- All I/O-bound operations are `async Task`/`async Task<T>` with `CancellationToken` propagation
- Parallel topic research via `Task.WhenAll` with per-topic exception isolation
- Job queue: `System.Threading.Channels.Channel<Guid>` (unbounded, single reader)
- Background processing: `BackgroundService` base class
- Scheduled research: `PeriodicTimer` with NCrontab cron parsing
- Per-job cancellation via `JobCancellationService` (registers/cancels `CancellationTokenSource` per job ID)

### Observability

- **OpenTelemetry**: `ActivitySource` spans per pipeline stage, OTLP export, console export for dev
- **Metrics**: Counters for jobs started/completed/failed, LLM calls/tokens, search cache hits/misses. Histogram for job duration.
- **Structured logging**: `ILogger<T>` with `LoggerMessage` source generators (e.g., `LogTopicFailed`)

## Important Files

| File | Why It Matters |
|---|---|
| `src/ResearchHarness.Web/Program.cs` | **Start here.** DI composition root — reveals the full dependency graph, config binding, HTTP client setup, DI lifetimes. |
| `src/ResearchHarness.Web/appsettings.json` | Configuration schema: LLM provider, API keys, model names, research limits, Redis, OpenTelemetry. |
| `src/ResearchHarness.Orchestration/ResearchOrchestrator.cs` | Core pipeline logic: consulting → decomposition → parallel research → peer review → assembly. |
| `src/ResearchHarness.Agents/Internal/AgentDtos.cs` | All LLM response DTOs. Changes here affect deserialization across the entire pipeline. |
| `src/ResearchHarness.Agents/AgentSerializerOptions.cs` | Centralized JSON serializer options. The `SnakeCaseLower` policy here is load-bearing. |
| `src/ResearchHarness.Infrastructure/Llm/OpenRouterLlmClient.cs` | Primary LLM client: tool-use structured output, retry logic, timeout, JSON repair. |
| `src/ResearchHarness.Core/Configuration/JobConfiguration.cs` | Job defaults: model names, limits, timeouts. All configurable via `Research:*` section. |
| `src/ResearchHarness.Core/Interfaces/ILlmClient.cs` | Central abstraction: `CompleteAsync<T>` (structured) and `CompleteAsync` (text). All agents depend on this. |
| `skills/researchharness/SKILL.md` | Canonical orientation document for AI agents. Pipeline overview, key files, critical rules. |
| `skills/researchharness/debugging.md` | Runtime failure patterns, log signatures, model compatibility list. |

## Runtime and Tooling

- **Runtime:** .NET 10 (SDK 10.0.103, `global.json`)
- **Solution format:** `.slnx` (new XML solution format)
- **Build system:** MSBuild via `dotnet build`
- **IDE:** JetBrains Rider (`.idea/` present, `ResearchHarness.sln.DotSettings.user`)
- **Web server:** Kestrel on `http://localhost:5000` (Development profile, no HTTPS)
- **API docs:** Scalar at `/scalar/v1` (Swagger alternative)
- **Persistence:** SQLite (`researchharness.db`), jobs stored as JSON blobs
- **Caching:** In-memory by default, optional Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`)
- **Test runner:** Microsoft.Testing.Platform (configured in `global.json`)
- **Secrets:** User secrets on Web project (ID: `867cd454-fbde-4ef2-b1b3-fa9e77be61ae`), `appsettings.Development.json` is gitignored
- **Pandoc 3.9** installed locally for reading the `.docx` design document
- **No CI/CD pipeline, no Docker, no `.editorconfig`**

## Testing

### Stack

| Tool | Version | Purpose |
|---|---|---|
| TUnit | 1.19.0 | Test framework (uses `[Test]`, `[Before(Test)]` — NOT xUnit/NUnit/MSTest) |
| AwesomeAssertions | 9.4.0 | Fluent assertions (FluentAssertions successor): `.Should().Be()`, `.Should().HaveCount()`, etc. |
| NSubstitute | 5.3.0 | Mocking: `Substitute.For<T>()`, `.Returns()`, `.Received()`, `Arg.Any<T>()`, `Arg.Is<T>()` |

### Test Structure

**Unit tests** (`tests/ResearchHarness.Tests.Unit/`): organized by layer — `Agents/`, `Infrastructure/`, `Orchestration/`.

**Integration tests** (`tests/ResearchHarness.Tests.Integration/`): use `WebApplicationFactory<Program>` with DI service replacement.

### Test Patterns

- **Setup:** `[Before(Test)]` method (not constructor, not `[SetUp]`)
- **Style:** Arrange/Act/Assert
- **Naming:** `MethodName_Scenario_ExpectedBehavior`
- **Mocking boundary:** LLM calls are mocked at `ILlmClient` (unit) or replaced via DI with `MockLlmClient` (integration)
- **HTTP mocking:** Custom `FakeHttpHandler` for LLM/search client tests
- **Test doubles:**
  - `InMemoryJobStore` (custom `IJobStore` in test project) for persistence tests
  - `MockLlmClient` (queue-based `ILlmClient`) for integration tests
  - `FakeHttpHandler` (inline in test files) for HTTP transport tests
- **No real external service calls in tests.** All LLM and search interactions are mocked.

### Test Commands

```powershell
# Unit tests only (via tools script)
powershell -File tools\run-tests.ps1

# Integration tests (manual)
dotnet test tests\ResearchHarness.Tests.Integration\ResearchHarness.Tests.Integration.csproj

# Model compatibility tests (gated, requires real API keys)
$env:RUN_MODEL_COMPAT_TESTS = "true"
dotnet test tests\ResearchHarness.Tests.Integration\ResearchHarness.Tests.Integration.csproj
```

### InternalsVisibleTo

- `ResearchHarness.Agents` → `ResearchHarness.Tests.Unit` (exposes internal DTOs and `AgentSerializerOptions`)
- `ResearchHarness.Infrastructure` → `ResearchHarness.Tests.Unit`
- `ResearchHarness.Web` → `ResearchHarness.Tests.Integration`

## Configuration Reference

`appsettings.json` structure (API keys are empty in committed config; use user secrets or `appsettings.Development.json`):

```json
{
  "Llm": { "Provider": "OpenRouter" },
  "OpenRouter": {
    "ApiKey": "", "BaseUrl": "https://openrouter.ai/api",
    "MaxConcurrentLlmCalls": 10, "MaxRetries": 3
  },
  "Anthropic": {
    "ApiKey": "", "BaseUrl": "https://api.anthropic.com",
    "Version": "2023-06-01", "MaxConcurrentLlmCalls": 10, "MaxRetries": 3
  },
  "BraveSearch": {
    "ApiKey": "", "CacheTtl": "24:00:00", "PageFetchTimeoutSeconds": 15
  },
  "Research": {
    "MaxTopics": 5, "MaxLabAgentsPerPI": 5, "MaxRevisionsPerPaper": 3,
    "PeerReviewerCount": 2, "SearchResultsPerQuery": 10,
    "EnableConsultingFirm": false,
    "LeadModel": "minimax/minimax-m2.5",
    "PIModel": "meta-llama/llama-3.3-70b-instruct",
    "LabModel": "meta-llama/llama-3.3-70b-instruct",
    "ReviewerModel": "meta-llama/llama-3.3-70b-instruct"
  },
  "ConnectionStrings": { "Jobs": "Data Source=researchharness.db" },
  "Redis": { "ConnectionString": "" },
  "OpenTelemetry": { "OtlpEndpoint": "" },
  "RateLimit": { "LlmConcurrency": 10, "SearchConcurrency": 5 }
}
```

## See Also

- `skill://researchharness/debugging.md` — runtime failure patterns and mitigations
- `skill://researchharness/api-reference.md` — HTTP API endpoint reference
