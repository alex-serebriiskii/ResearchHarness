# Phase 2 TODOs

## Reliability / Correctness

1. **JSON repair at LLM client boundary** — when a model returns array fields as JSON-encoded strings instead of JSON arrays (confirmed with 8B models), attempt `JsonSerializer.Deserialize<JsonElement>` on the string value and re-deserialize. This enables cheaper model fallback without crashing.

2. **Exhaustive nullable DTO coverage** — add a unit test that deserializes a JSON payload with every snake_case field present (including nested arrays) and asserts all fields round-trip correctly via SnakeCaseLower policy. Prevents regression if new DTOs are added without the policy.

3. **Unit tests for JournalAssemblyOutput and PaperSynthesisOutput null-coalescing** — assert that `?? ""` guards fire when the model returns null for required string fields.

## Performance

4. **Parallel PI execution** — Phase 1 processes topics sequentially. When MaxTopics > 1 (Phase 2), PIs must run in parallel up to RateLimitedExecutor concurrency. Wire through CancellationToken propagation.

5. **Per-call adaptive timeout** — instead of a flat 300s HttpClient timeout, estimate max response time from input token count (rough rule: 1 token/s + 10s overhead) and set a per-request CancellationToken deadline. Prevents slow responses from blocking the pipeline unnecessarily.

6. **Remove Phase 1 MaxTopics cap** — `InstituteLeadAgent.DecomposeThemeAsync` has `int topicsToRequest = Math.Min(config.MaxTopics, 1)`. This must be removed in Phase 2.

## Persistence

7. **SQL persistence** — replace InMemoryJobStore with a SQLite (or SQL Server) backed store. Jobs must survive app restarts. Schema: jobs table (id, theme, status, created_at, completed_at), papers table, findings table, sources table.

8. **Redis cache for search results** — BraveSearch results for identical queries within a TTL window should not re-bill. Add IDistributedCache layer in front of BraveSearchProvider.

## Pipeline Stages (Phase 2 Agents)

9. **ConsultingFirmService** — implement and enable (currently gated by EnableConsultingFirm=false config flag). Accepts a Paper and returns a ConsultingReport with strategic analysis.

10. **PeerReviewService + revision loop** — reviewer LLM critiques each Paper; if confidence < threshold, PI revises and re-submits for review up to MaxRevisions times. Wire revision count into Paper.RevisionCount.

11. **AnthropicLlmClient** — implement ILlmClient backed by Anthropic's native API (not via OpenRouter). Required before any Anthropic key is configured.

## Observability

12. **Structured logging** — replace plain string log messages with structured events (LoggerMessage source generators). Add correlation IDs (jobId, topicId, searchTaskIndex) to every log scope.

13. **OpenTelemetry traces** — add ActivitySource spans for each pipeline stage (Decompose, PI, Lab per task, Synthesis, Review, Assemble). Export to OTLP or console in dev.

## Model Strategy

14. **Model selection and fallback strategy** — document and implement tier-based model selection: premium (GPT-4o, Claude 3.5 Sonnet) for Lead/PI synthesis, economy (Llama 3.3 70B) for Lab extraction. Add fallback: if primary model returns HTTP 429 and no Retry-After, rotate to secondary before applying delay.

15. **Free-tier model compatibility test harness** — automate the tool_call probe used during live testing. For each model in a candidate list, send a minimal tool_call request and assert the response contains a tool_calls block with non-null arguments. Run on CI or on-demand before model config changes.

## Developer Experience

16. **Integration tests** — test the full pipeline against a mock ILlmClient and mock ISearchProvider that return deterministic fixtures. Assert journal structure, finding count, bibliography deduplication, and synthesis non-empty. These replace the current reliance on live end-to-end runs as the only correctness signal.

17. **API key guard for non-/internal/ routes** — currently ApiKeyMiddleware only covers /internal/. Audit all routes and confirm no data leaks through unauthenticated endpoints.
