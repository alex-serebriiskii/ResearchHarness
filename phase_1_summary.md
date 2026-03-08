# Phase 1 Summary — Agentic Research Harness

## What Was Built

A working vertical slice of the agentic research pipeline: single-topic decomposition, lab agent search and extraction, PI synthesis, and journal assembly. All in-memory. No peer review. No consulting firm.

**7 projects · 58 source files · 0 build warnings · 37/37 unit tests passing**

---

## Project Structure

```
ResearchHarness/
├── global.json                          # SDK pin + MTP test runner config
├── Directory.Build.props                # TestingPlatformDotnetTestSupport for all test projects
├── phase_1_summary.md                   # this file
├── src/
│   ├── ResearchHarness.Core/            # Domain models, interfaces, LLM contracts
│   │   ├── Configuration/
│   │   │   └── JobConfiguration.cs
│   │   ├── Interfaces/
│   │   │   ├── IConsultingFirmService.cs   (stub — Phase 2)
│   │   │   ├── IInstituteLeadAgent.cs
│   │   │   ├── IJobStore.cs
│   │   │   ├── ILabAgentService.cs
│   │   │   ├── ILlmClient.cs
│   │   │   ├── IPageFetcher.cs
│   │   │   ├── IPeerReviewService.cs       (stub — Phase 2)
│   │   │   ├── IPrincipalInvestigatorAgent.cs
│   │   │   ├── IResearchOrchestrator.cs
│   │   │   └── ISearchProvider.cs
│   │   ├── Llm/
│   │   │   ├── LlmRequest.cs
│   │   │   └── LlmResponse.cs
│   │   └── Models/
│   │       ├── Finding.cs
│   │       ├── Journal.cs
│   │       ├── JobStatus.cs
│   │       ├── Paper.cs
│   │       ├── ResearchJob.cs
│   │       ├── ResearchTopic.cs
│   │       ├── ReviewResult.cs
│   │       ├── ReviewVerdict.cs
│   │       ├── SearchModels.cs           (SearchResults, SearchHit, PageContent, SearchOptions)
│   │       ├── SearchTask.cs
│   │       ├── Source.cs
│   │       ├── SourceCredibility.cs
│   │       └── TopicStatus.cs
│   ├── ResearchHarness.Agents/          # Agent implementations and prompt factories
│   │   ├── AgentSerializerOptions.cs
│   │   ├── InstituteLeadAgent.cs
│   │   ├── LabAgentService.cs
│   │   ├── PrincipalInvestigatorAgent.cs
│   │   ├── Internal/
│   │   │   └── AgentDtos.cs             (internal LLM output records)
│   │   └── Prompts/
│   │       ├── JournalAssemblyPrompt.cs
│   │       ├── LabExtractionPrompt.cs
│   │       ├── LeadDecompositionPrompt.cs
│   │       └── PITaskBreakdownPrompt.cs
│   ├── ResearchHarness.Infrastructure/  # External integrations
│   │   ├── Llm/
│   │   │   ├── AnthropicLlmClient.cs
│   │   │   ├── AnthropicOptions.cs
│   │   │   ├── LlmException.cs
│   │   │   └── RateLimitedExecutor.cs
│   │   ├── Persistence/
│   │   │   └── InMemoryJobStore.cs
│   │   └── Search/
│   │       ├── BravePageFetcher.cs
│   │       ├── BraveSearchOptions.cs
│   │       ├── BraveSearchProvider.cs
│   │       ├── SearchResultCache.cs
│   │       └── Dto/
│   │           └── BraveSearchResponse.cs
│   ├── ResearchHarness.Orchestration/  # Pipeline coordination
│   │   ├── ResearchJobProcessor.cs      (BackgroundService)
│   │   └── ResearchOrchestrator.cs
│   └── ResearchHarness.Web/            # ASP.NET host
│       ├── ApiKeyMiddleware.cs
│       ├── Program.cs
│       ├── appsettings.json
│       └── Controllers/
│           └── ResearchController.cs
└── tests/
    ├── ResearchHarness.Tests.Unit/
    │   ├── Agents/
    │   │   ├── InstituteLeadAgentTests.cs       (5 tests)
    │   │   ├── LabAgentServiceTests.cs           (9 tests)
    │   │   └── PrincipalInvestigatorAgentTests.cs (6 tests)
    │   ├── Infrastructure/
    │   │   └── InMemoryJobStoreTests.cs          (9 tests)
    │   └── Orchestration/
    │       └── ResearchOrchestratorTests.cs      (5 tests — 3 additional assertions)
    └── ResearchHarness.Tests.Integration/
        └── PipelineIntegrationTests.cs           (scaffold — Phase 2)
```

---

## Dependency Graph

```
Core  ←──  Agents          (depends on Core)
Core  ←──  Infrastructure  (depends on Core)
Core
Agents
Infrastructure  ←──  Orchestration  (depends on all three)

Core
Orchestration   ←──  Web            (depends on Core + Orchestration for DI)

Tests.Unit        →  Core, Agents, Infrastructure, Orchestration
Tests.Integration →  all src projects
```

---

## Data Model Fixes Applied

The design doc had four issues that were corrected before writing any code:

| Fix | Problem | Resolution |
|---|---|---|
| `Source.SourceId` | `Finding.SourceRefs` references sources by `Guid` but the doc's `Source` record had no Id field | Added `Guid SourceId` as first positional parameter |
| `Paper.TopicId` | Paper has no link back to its originating topic | Added `Guid TopicId` as first positional parameter |
| `JobConfiguration.JobTimeout` | Default of `TimeSpan.Zero` = instant timeout | Changed to `TimeSpan? JobTimeout`; `EffectiveJobTimeout` property resolves `null` to 30 minutes |
| `ISearchProvider` split | `FetchPageAsync` lumped into the search interface despite different failure modes and rate limits | Moved to a separate `IPageFetcher` interface |

---

## Key Design Decisions

### LLM structured output via tool use

`AnthropicLlmClient.CompleteAsync<T>` sends a tool definition whose `input_schema` is the caller-supplied `JsonObject`. The Anthropic API then guarantees a `tool_use` content block matching that schema. No regex parsing, no fallback heuristics — a schema violation is a hard failure logged with the raw response.

### `ILabAgentServiceInternal`

`ILabAgentService.ExecuteSearchTaskAsync` returns `List<Finding>`. The PI also needs the `List<Source>` objects to build the bibliography. Rather than a side-channel registry or changing the Core interface, a second interface `ILabAgentServiceInternal : ILabAgentService` adds `ExecuteSearchTaskFullAsync` returning `LabTaskResult(Findings, Sources)`. `PrincipalInvestigatorAgent` injects `ILabAgentServiceInternal`. DI registers `LabAgentService` as `ILabAgentServiceInternal` and resolves `ILabAgentService` via that registration.

### `JobConfiguration` injected directly

`InstituteLeadAgent` receives a `JobConfiguration` singleton (bound from config in `Program.cs`). This avoids pulling `Microsoft.Extensions.Options` into the Agents project, which only depends on `Microsoft.Extensions.Logging.Abstractions`.

### `Channel<Guid>` as the job queue

`ResearchOrchestrator.StartResearchAsync` writes the job ID to an `UnboundedChannel<Guid>`. `ResearchJobProcessor` (a `BackgroundService`) drains it, creating a DI scope per job so scoped services resolve correctly. One job runs at a time in Phase 1.

### `dotnet test` on .NET 10 SDK

TUnit uses Microsoft.Testing.Platform, which dropped VSTest support on .NET 10 SDK. The correct invocation requires `global.json` with `"test": {"runner": "Microsoft.Testing.Platform"}`. Tests then run as:

```bash
dotnet test --project tests/ResearchHarness.Tests.Unit/ResearchHarness.Tests.Unit.csproj
dotnet run --project tests/ResearchHarness.Tests.Unit/ResearchHarness.Tests.Unit.csproj
```

Both work. `Directory.Build.props` also sets `TestingPlatformDotnetTestSupport=true` for forward compatibility.

---

## Configuration

`appsettings.json` keys that must be populated before running:

| Key | Where to get it |
|---|---|
| `Anthropic.ApiKey` | https://console.anthropic.com |
| `BraveSearch.ApiKey` | https://api.search.brave.com |
| `ApiKey` | any string — used as `X-Api-Key` header for `/internal/` routes |

Default model configuration (Phase 1):

| Role | Model |
|---|---|
| Institute Lead | `claude-sonnet-4-20250514` |
| Principal Investigator | `claude-sonnet-4-20250514` |
| Lab Agent | `claude-haiku-4-5-20251001` |

---

## Running the Service

```bash
cd src/ResearchHarness.Web
dotnet run
```

### Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/internal/research/start` | Start a job. Body: `{"theme":"..."}`. Returns `Guid`. |
| `GET` | `/internal/research/{jobId}/status` | Returns `JobStatus` enum value. |
| `GET` | `/internal/research/{jobId}/journal` | Returns completed `Journal` JSON, or `409` if not yet complete. |

All `/internal/` routes require `X-Api-Key` header matching the configured `ApiKey` value. If `ApiKey` is empty in config, the check is skipped (dev convenience).

### Smoke test

```bash
# Start a job
curl -X POST http://localhost:5000/internal/research/start \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: <your-key>" \
  -d '{"theme":"cardiovascular drug pipeline 2025"}'
# → "3fa85f64-5717-4562-b3fc-2c963f66afa6"

# Poll until Completed
curl http://localhost:5000/internal/research/3fa85f64-.../status \
  -H "X-Api-Key: <your-key>"

# Retrieve journal
curl http://localhost:5000/internal/research/3fa85f64-.../journal \
  -H "X-Api-Key: <your-key>"
```

---

## Running Tests

```bash
# Unit tests
dotnet test --project tests/ResearchHarness.Tests.Unit/ResearchHarness.Tests.Unit.csproj

# Integration scaffold
dotnet test --project tests/ResearchHarness.Tests.Integration/ResearchHarness.Tests.Integration.csproj

# All at once (discovers via global.json)
dotnet test
```

**Current results: 37 unit + 1 integration = 38 tests, 0 failures.**

---

## What Phase 2 Needs to Add

| Area | Work |
|---|---|
| Persistence | Replace `InMemoryJobStore` with `SqlJobStore` (EF Core + SQL Server) |
| Consulting Firm | Implement `ConsultingFirmService`; wire `EnableConsultingFirm` flag in orchestrator |
| Peer Review | Implement `PeerReviewService`; add review loop with `MaxRevisionsPerPaper` cap to orchestrator |
| Parallelism | Dispatch lab agents concurrently (bounded by `MaxLabAgentsPerPI` semaphore) in `PrincipalInvestigatorAgent` |
| Multi-topic | Remove Phase 1 cap of 1 topic; run PIs in parallel across topics |
| Integration tests | `WebApplicationFactory`-based tests against the full pipeline (API key env-gated) |
| Observability | Structured logging with per-job correlation IDs; token cost accumulator per job |
| Caching | Redis `IDistributedCache` to back `SearchResultCache` across restarts |
