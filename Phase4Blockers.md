# Phase 4 Blockers

Items from Phase 2 and Phase 3 that must be resolved before starting Phase 4 (Website Integration).

**Status: 7 of 8 blockers resolved. 1 remains (model compat harness).**

---

## Phase 2 — Open Items

### 1. JSON repair at LLM client boundary — ✅ RESOLVED
`LlmJsonRepair.RepairStringifiedJsonFields` now handles nested objects, arrays containing objects, and deeply nested stringified fields via recursive traversal. Covered by 11 unit tests (3 new: nested array, deeply nested field, array-element field).

### 2. Exhaustive nullable DTO coverage test — ✅ RESOLVED (pre-existing)
`AgentDtoTests.cs` has 12 tests covering all 8 DTO records: snake_case mapping, nullable fields, round-trip correctness. No code changes needed.

### 3. Unit tests for null-coalescing guards — ✅ RESOLVED
6 new tests across `InstituteLeadAgentTests`, `PrincipalInvestigatorAgentTests`, and `LabAgentServiceTests` verify that null DTO fields produce safe defaults (`""` / `[]`) in domain models. Covers all guard sites: topic list/string fields, synthesis summary, task breakdown source types, lab extraction findings/sources.

### 4. ConsultingFirmService — ✅ RESOLVED (pre-existing)
Fully implemented in `ConsultingFirmService.cs`, DI-registered, 4 unit tests, orchestrator integration tested. Gated by `EnableConsultingFirm=false` in appsettings — a config decision, not a code gap.

### 5. Free-tier model compatibility test harness — ❌ REMAINING
Not built. Requires real API keys and environment gating. Medium effort — deferred to pre-Phase 4 hardening.

### 6. Integration tests — ✅ RESOLVED
2 full pipeline integration tests added in `FullPipelineIntegrationTests.cs`: (1) submit job with mocked LLM/search, poll to completion, verify journal structure (papers, findings, bibliography, summaries); (2) same with peer review enabled, verifying review cycle runs and reviews appear in output. Uses `StubSearchProvider` and `StubPageFetcher` to prevent real HTTP calls.

### 7. API key guard audit — ✅ RESOLVED
3 integration tests in `PipelineIntegrationTests.cs` verify: (1) `/health` is accessible without API key; (2) `/openapi/v1.json` returns 404 in Production; (3) `/scalar/v1` returns 404 in Production. Auth boundary confirmed correct.

---

## Phase 3 — Open Items

### 8. Admin dashboard — ✅ RESOLVED
Journal Viewer implemented at `/admin/journals` with three-column layout (job list, journal detail, run metrics). Renders OverallSummary, CrossTopicAnalysis, paper executive summaries, findings, reviews, and bibliography with markdown formatting (Markdig). Collapsible paper cards and findings sections. Credibility/confidence/relevance color-coded tags. Cost and pipeline metrics pane.

---

## Summary of Remaining Blockers

| # | Blocker | Effort | Priority |
|---|---------|--------|----------|
| 5 | Model compatibility test harness | Medium | High — needed before switching models |


## Phase 4 — Scope (from design doc Section 13)

For reference, Phase 4 deliverables as defined in the design document:

1. **Public insights API** — `InsightsController` serving pre-computed journals (read-only, no research triggers).
2. **Content generation pipeline** — transform raw Journal JSON into website-ready content.
3. **Journal caching layer** — efficient serving of completed journals to frontend visitors.
4. **Semantic/vector search** (stretch) — search over accumulated research outputs. This is the trigger for revisiting Semantic Kernel adoption (see `design_considerations.md`).
