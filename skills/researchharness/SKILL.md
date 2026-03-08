---
name: researchharness
description: Architecture, development workflow, and debugging guide for the ResearchHarness .NET 10 agentic research pipeline
alwaysApply: false
---

## 1. Overview

ResearchHarness is a .NET 10 multi-agent research pipeline. A theme is decomposed by InstituteLeadAgent into ResearchTopics; each topic is researched by a PrincipalInvestigatorAgent that breaks it into SearchTasks executed by LabAgentService (Brave Search + optional page fetch + LLM extraction); the PI synthesizes findings into a Paper; the Lead assembles Papers into a Journal.

## 2. Solution Structure

| Project | Responsibility |
|---|---|
| ResearchHarness.Core | Domain models, interfaces, no NuGet deps |
| ResearchHarness.Infrastructure | LLM client (OpenRouter), Search (Brave), Persistence (in-memory) |
| ResearchHarness.Agents | InstituteLeadAgent, PrincipalInvestigatorAgent, LabAgentService, prompt factories |
| ResearchHarness.Orchestration | ResearchOrchestrator, ResearchJobProcessor (BackgroundService) |
| ResearchHarness.Web | ASP.NET minimal host, Program.cs DI wiring, controllers, ApiKeyMiddleware |
| ResearchHarness.Tests.Unit | TUnit 1.19, 51 tests (Microsoft.Testing.Platform) |

## 3. Key Files

- `src/ResearchHarness.Web/appsettings.json` — config schema template (empty keys committed)
- `src/ResearchHarness.Web/appsettings.Development.json` — real keys (gitignored)
- `src/ResearchHarness.Web/Program.cs` — DI wiring, HttpClient registration, controller setup
- `src/ResearchHarness.Infrastructure/Llm/OpenRouterLlmClient.cs` — all LLM communication; structured output via tool_call; snake_case deserialization
- `src/ResearchHarness.Infrastructure/Llm/OpenRouterOptions.cs` — timeout, retry, concurrency config
- `src/ResearchHarness.Agents/Internal/AgentDtos.cs` — LLM response DTOs; all string fields nullable, all List fields nullable
- `src/ResearchHarness.Agents/Prompts/` — prompt factories (LeadDecompositionPrompt, PITaskBreakdownPrompt, LabExtractionPrompt, JournalAssemblyPrompt)
- `src/ResearchHarness.Agents/LabAgentService.cs` — search + extraction + null-guard mapping

## 4. Development Workflow

Stop the app before building (Windows DLL locking):

```
powershell -File tools\stop-app.ps1
```

Build:

```
powershell -File tools\build.ps1
```

Run tests:

```
powershell -File tools\run-tests.ps1
```

Start the app:

```
powershell -File tools\start-app.ps1
```

Submit a research job:

```
powershell -File tools\submit-job.ps1 -Theme "your query"
```

Poll status then retrieve results:

```
powershell -File tools\poll-job.ps1 -JobId {id}
powershell -File tools\show-journal.ps1 -JobId {id}
```

**TUnit CLI note:** do NOT pass `--nologo` — TUnit on Microsoft.Testing.Platform does not support that flag.

## 5. LLM Integration Rules

**CRITICAL — violations cause silent data loss, not exceptions.**

- All LLM DTOs: every `string` field MUST be declared `string?`, every list MUST be `List<T>?`
- Mapping sites: coalesce lists with `?? []`, strings with `?? ""`
- `UserDeserializeOptions` MUST set BOTH options:
  ```csharp
  PropertyNameCaseInsensitive = true,
  PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  ```
  Without `SnakeCaseLower`, every snake_case field silently deserializes as null — no exception is thrown.
- All JSON schemas use snake_case (OpenAI tool-call convention); C# records are PascalCase — the naming policy bridges them automatically.
- OpenRouter HttpClient timeout MUST be 300 seconds. The default 100s is too short for large-context synthesis calls.

## 6. Configuration Reference

`src/ResearchHarness.Web/appsettings.json` shape:

```json
{
  "Llm": { "Provider": "OpenRouter" },
  "OpenRouter": {
    "ApiKey": "",
    "BaseUrl": "https://openrouter.ai/api",
    "MaxRetries": 5,
    "MaxConcurrentLlmCalls": 3,
    "RateLimitRetryBaseDelaySeconds": 20.0
  },
  "BraveSearch": { "ApiKey": "", "PageFetchTimeoutSeconds": 15 },
  "Research": {
    "LeadModel": "meta-llama/llama-3.3-70b-instruct",
    "PIModel": "meta-llama/llama-3.3-70b-instruct",
    "LabModel": "meta-llama/llama-3.3-70b-instruct",
    "MaxTopics": 1,
    "MaxLabAgentsPerPI": 5,
    "EnableConsultingFirm": false
  }
}
```

**Phase 1 cap:** `MaxTopics` is capped to 1 in code regardless of the config value.

## 7. See Also

- `skill://researchharness/debugging.md` — runtime failure patterns and mitigations
- `skill://researchharness/api-reference.md` — HTTP API endpoint reference
