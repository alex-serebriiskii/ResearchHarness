# ResearchHarness

An agentic research pipeline built on .NET 10 that decomposes a broad research theme into structured, peer-reviewed findings with full source attribution. Submit a topic, and a hierarchy of LLM-powered agents will search the web, extract facts, synthesize papers, run peer review, and assemble a final research journal.

## Architecture

```
Theme
  |
  v
Institute Lead ---- decomposes theme into research topics
  |
  v (per topic, in parallel)
Principal Investigator ---- breaks topic into search tasks
  |
  v (per task, concurrent up to MaxLabAgentsPerPI)
Lab Agents ---- execute web searches, fetch pages, extract findings
  |
  v
PI Synthesis ---- assembles findings into a Paper with bibliography
  |
  v
Peer Review ---- LLM reviewers critique the paper (revision loop)
  |
  v
Consulting Firm ---- optional strategic analysis layer
  |
  v
Journal Assembly ---- cross-topic analysis, executive summaries, master bibliography
```

### Project Structure

```
ResearchHarness/
├── src/
│   ├── ResearchHarness.Core/             # Domain models, interfaces, zero external dependencies
│   ├── ResearchHarness.Agents/           # Agent implementations and prompt factories
│   ├── ResearchHarness.Infrastructure/   # LLM clients, Brave Search, SQLite persistence, caching
│   ├── ResearchHarness.Orchestration/    # Pipeline coordination, job processing, scheduling
│   └── ResearchHarness.Web/             # ASP.NET host, API controllers, admin UI (Blazor SSR)
├── tests/
│   ├── ResearchHarness.Tests.Unit/       # 112 unit tests
│   └── ResearchHarness.Tests.Integration/
└── tools/                                # PowerShell helper scripts
```

### Dependency Graph

```
Core  <--  Agents
Core  <--  Infrastructure
Core + Agents + Infrastructure  <--  Orchestration
Core + Orchestration  <--  Web
```

`Core` carries zero NuGet dependencies. All external concerns (HTTP clients, databases, caching) live in `Infrastructure`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.103 or later)
- At least one LLM provider API key:
  - [OpenRouter](https://openrouter.ai/) (recommended -- routes to multiple model providers)
  - [Anthropic](https://console.anthropic.com/) (direct Claude API access)
- [Brave Search API key](https://api.search.brave.com/) for web search

Optional:
- Redis for distributed search result caching
- An OpenTelemetry collector for traces/metrics

## Configuration

The committed `appsettings.json` contains the full configuration schema with all keys set to empty strings. Populate real values using any standard ASP.NET Core configuration source:

**User Secrets (recommended for local development):**

```bash
cd src/ResearchHarness.Web
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-..."
dotnet user-secrets set "BraveSearch:ApiKey" "BSA..."
dotnet user-secrets set "ApiKey" "any-string-for-internal-auth"
```

**Environment variables:**

```bash
export OpenRouter__ApiKey="sk-or-v1-..."
export BraveSearch__ApiKey="BSA..."
export ApiKey="any-string-for-internal-auth"
```

### Key Configuration Sections

| Section | Purpose |
|---|---|
| `Llm.Provider` | `OpenRouter` or `Anthropic` -- selects the active LLM client |
| `OpenRouter.*` | OpenRouter API key, base URL, concurrency, retries, fallback models |
| `Anthropic.*` | Anthropic API key, base URL, API version |
| `BraveSearch.*` | Search API key, cache TTL, page fetch timeout |
| `Research.*` | Pipeline tuning: max topics, agents per PI, revision rounds, model selection per role |
| `ApiKey` | Shared secret for `X-Api-Key` header on `/internal/` routes (empty = auth disabled) |
| `ConnectionStrings.Jobs` | SQLite connection string for job persistence |

## Getting Started

```bash
# Build the solution
dotnet build

# Start the web host
dotnet run --project src/ResearchHarness.Web

# Or use the helper script (builds, starts in background, polls for readiness)
./tools/start-app.ps1
```

The API will be available at `http://localhost:5000`. Interactive API documentation is served at `/scalar/v1`.

## API Reference

All endpoints are under `/internal/research` and require the `X-Api-Key` header when `ApiKey` is configured.

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/internal/research/start` | Start a research job. Body: `{"theme": "..."}`. Returns job ID. |
| `GET` | `/internal/research/{jobId}/status` | Poll job status (`Pending`, `Running`, `Completed`, `Failed`). |
| `GET` | `/internal/research/{jobId}/journal` | Retrieve the completed research journal. 409 if still running. |
| `GET` | `/internal/research/{jobId}/cost` | Token usage and cost summary for a completed job. |
| `GET` | `/internal/research/jobs` | List jobs with optional `?status=`, `?offset=`, `?limit=` filters. |
| `POST` | `/internal/research/{jobId}/cancel` | Cancel a running job. |

### Quick Smoke Test

```bash
# Submit a research job
JOB_ID=$(curl -s -X POST http://localhost:5000/internal/research/start \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{"theme": "advances in mRNA vaccine delivery 2025"}')

# Poll status
curl -s http://localhost:5000/internal/research/$JOB_ID/status \
  -H "X-Api-Key: your-key"

# Retrieve the journal when complete
curl -s http://localhost:5000/internal/research/$JOB_ID/journal \
  -H "X-Api-Key: your-key"
```

Or use the PowerShell helpers:

```powershell
$jobId = ./tools/submit-job.ps1 -Theme "advances in mRNA vaccine delivery 2025"
./tools/poll-job.ps1 -JobId $jobId
./tools/show-journal.ps1 -JobId $jobId
```

## Admin UI

A Blazor SSR admin interface is available at `/admin` providing:

- **Research Console** -- submit jobs and monitor progress in real time
- **Journal Viewer** -- browse completed research journals with rendered Markdown

## Running Tests

```bash
# Unit tests (112 tests)
dotnet test --project tests/ResearchHarness.Tests.Unit

# Integration tests
dotnet test --project tests/ResearchHarness.Tests.Integration

# All tests
dotnet test
```

## LLM Provider Support

ResearchHarness supports two LLM backends:

- **OpenRouter** -- access to 200+ models through a single API. Each agent role (Lead, PI, Lab, Reviewer) can be configured to use a different model.
- **Anthropic** -- direct access to Claude models via the native API with tool-use structured output.

Model assignments are configured per agent role in `Research.*Model` settings. The pipeline uses tool-use (function calling) to guarantee structured JSON output from every LLM call.

## License

This project is not currently licensed for redistribution. All rights reserved.
