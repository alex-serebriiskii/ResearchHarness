# Design Considerations

A running record of architectural questions, tradeoff analyses, and decisions made during development. Each entry captures the context at the time it was raised so future maintainers understand not just what was decided but why, and under what conditions the decision should be revisited.

---

## 2026-03-08 — Semantic Kernel adoption

### Question

Should the project migrate to Microsoft Semantic Kernel before continuing Phase 2 build-out?

### Context at time of consideration

- Phase 1 complete: single-topic vertical slice, 7 projects, 58 source files, 37/37 unit tests passing.
- Stack: .NET 10, hand-rolled, zero framework dependencies in Core, only `Microsoft.Extensions.*` elsewhere.
- LLM integration: `ILlmClient.CompleteAsync<T>` backed by `OpenRouterLlmClient` — raw `HttpClient`, Anthropic tool-use structured output, retry/backoff, token tracking.
- Agent hierarchy: `InstituteLeadAgent → PrincipalInvestigatorAgent → LabAgentService`, each behind a domain interface, mocked at that boundary in unit tests.
- Phase 2 backlog: SQL persistence, ConsultingFirmService, PeerReviewService + revision loop, parallel PI execution, integration tests, structured logging, Redis cache.

### Analysis

**What SK provides and whether it applies:**

| SK feature | Applicability |
|---|---|
| Connector ecosystem (OpenAI, Azure OAI, Anthropic) | Low — OpenRouter already abstracts providers behind one endpoint |
| Plugin/function calling infrastructure | None — already solved by tool-use blocks via `ILlmClient` |
| `ChatCompletionAgent` / `AgentGroupChat` | Misfit — the pipeline is a directed workflow, not a conversation |
| Process Framework | Closest fit for pipeline stages, but marked experimental and incomplete |
| Prompt templating (Handlebars/Liquid) | No current value — prompt factories are typed, tested, and sufficient |
| Memory / vector search | Not in scope until Phase 4+ |
| Filters / middleware | Covered by the existing `RateLimitedExecutor` pattern |

**Upsides:**

1. Connector maintenance offloaded. API changes in Anthropic or OpenRouter wire format would be absorbed by SK's connectors rather than by `OpenRouterLlmClient`.
2. Ecosystem longevity. Microsoft investment is heavy; community plugins and documentation are growing.
3. Declarative prompt management at scale. When A/B testing prompt variants across many agents, SK's `.prompty` file approach is cleaner than multiplying factory classes.
4. Future memory/retrieval readiness. If semantic retrieval over accumulated journals is ever needed, SK's memory and vector abstractions are co-integrated with the kernel.

**Downsides:**

1. **API instability tax.** SK's agent framework and process framework carry `[Experimental]` attributes. `TreatWarningsAsErrors` is enabled project-wide — every experimental attribute is a compiler error. SK has a consistent history of breaking changes between minor versions.
2. **Domain mismatch.** `ChatCompletionAgent` is a single-turn chatbot wrapper; `AgentGroupChat` targets open-ended agent conversation. This project's pipeline is a strict directed workflow (decompose → research → review → assemble). Fitting it into SK's group-chat model requires adapters that negate the reason for adopting the framework in the first place.
3. **Test seams destroyed.** All 37 unit tests mock `ILlmClient` at a clean, cheap boundary. SK's `Kernel` is a large DI container; mocking through it is possible with NSubstitute but brittle, and SK's internal wiring can bypass mock seams. The full test infrastructure would need to be rebuilt.
4. **Structured output regression.** `CompleteAsync<T>` with `OutputSchema: JsonObject` guarantees typed deserialization via Anthropic tool-use blocks — tested and working. SK's structured output support is provider-dependent and not uniformly surfaced. The Anthropic SK connector is community-contributed and historically lags the official API.
5. **Migration delays Phase 2 directly.** All eight Phase 2 items are solvable with the current architecture. Migration would require touching all five agent implementations, the LLM client, all prompt factories, and all 37 unit tests before forward progress resumes — weeks of rework to land in roughly the same architectural place.
6. **Dependency surface expansion.** SK pulls in the OpenAI SDK, Azure SDK abstractions, and significant transitive weight. Core intentionally carries zero NuGet dependencies; SK compromises that layering constraint.
7. **OpenRouter routing edge cases.** SK's OpenAI connector handles standard OpenAI models well, but Anthropic models routed through OpenRouter's OpenAI-compatible endpoint have model-specific behaviors (tool-use schema format, stop reasons, token counting) that SK will not handle correctly — falling back to custom adapter code anyway.

### Decision

**Do not adopt Semantic Kernel at this stage.** The current `ILlmClient` abstraction already provides the provider isolation SK's connectors would give, with tighter control over the structured output contract. No Phase 2 work item is accelerated by SK.

### Conditions for revisiting

- **Semantic/vector retrieval is needed** over accumulated research outputs (likely Phase 4). At that point SK's memory integration avoids building embedding + vector-store plumbing from scratch.
- **Provider surface explodes** — five or more LLM providers with divergent APIs that cannot be unified through OpenRouter. SK's connector ecosystem then pays off. Until OpenRouter stops being a viable unified endpoint, this condition is not met.
