# ResearchHarness Debugging Guide

## 1. Before You Debug

Always check `run.log` and `run.err` first. Both files are written to the project root. `run.err` captures stderr; most LLM and deserialization failures appear there with full stack traces.

## 2. Job Status Meanings

| Status | Notes |
|---|---|
| Pending | Job accepted, not yet picked up by processor |
| Researching | LabAgentService tasks in flight |
| Synthesizing | PrincipalInvestigatorAgent writing Papers |
| Reviewing | Not used in Phase 1 |
| Assembling | InstituteLeadAgent assembling Papers into Journal |
| Completed | Journal available at /journal endpoint |
| Failed | Unrecoverable error; check run.err |

Status values are serialized as strings (not integers) in all API responses.

## 3. Common Failure Patterns

### `ArgumentNullException` on a List field

**Log signature:** `ArgumentNullException: Value cannot be null. (Parameter 'collection')`

**Cause:** LLM returned null for a list field; the mapping site passed it directly to a constructor expecting a non-null collection.

**Fix:** Check that the DTO declares `List<T>?` and that the mapping site coalesces with `?? []`.

---

### All string fields null in journal output

**Log signature:** No exception — journal fields are empty strings or null throughout.

**Cause:** `UserDeserializeOptions` is missing `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`. Every snake_case key from the model silently fails to match PascalCase C# properties.

**Fix:** Add `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` to the same options object that sets `PropertyNameCaseInsensitive = true`. Both are required.

---

### `TaskCanceledException: HttpClient.Timeout`

**Log signature:** `TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout`

**Cause:** OpenRouter HttpClient is using the default 100s timeout. Large synthesis prompts regularly exceed 100s.

**Fix:** Verify `Program.cs` sets `.Timeout = TimeSpan.FromSeconds(300)` on the OpenRouter `HttpClient`.

---

### `LlmException: response did not contain tool_calls`

**Cause:** The configured model does not honour `tool_choice: required`. It returns a plain `content` message instead.

**Fix:** The model is incompatible with structured output. Test candidate models with a minimal tool_call probe before changing config. See `phase_2_todos.md` item 15 for the probe pattern.

---

### `LlmException: Failed to deserialize function arguments`

**Log signature:** `JsonException` inside deserialization of function arguments.

**Cause:** The model (typically a sub-8B model) returned the arguments as a stringified JSON string inside the JSON object rather than an inline object.

**Fix:** Upgrade `LabModel` to a larger, confirmed-compatible model (see Section 5 for the compatibility list).

---

### HTTP 429 with no `Retry-After` header

**Cause:** Venice upstream rate limit. OpenRouter forwards 429 without a `Retry-After` header, so the client falls back to linear back-off.

**Fix:** `RateLimitRetryBaseDelaySeconds` in config controls the back-off interval (default 20s). Increase if 429s persist. `MaxRetries` controls attempt count.

---

### `Build FAILED: The process cannot access the file ... because it is being used by another process`

**Cause:** `ResearchHarness.Web` process is still running; Windows locks the output DLL.

**Fix:** Run `powershell -File tools\stop-app.ps1` before rebuilding.

## 4. Checking LLM Call Quality

Search `run.log` for lines matching `LLM call completed`. Each line logs token counts:

```
LLM call completed: {in} in, {out} out
```

If `out` is below 50 tokens on a synthesis or assembly call, the model almost certainly returned null-filled fields. Verify DTO nullability (`string?`, `List<T>?`) and that both `PropertyNameCaseInsensitive` and `SnakeCaseLower` are set on `UserDeserializeOptions`.

## 5. Free Model Compatibility

**Confirmed working with tool_calls:**
- `meta-llama/llama-3.3-70b-instruct:free`
- `mistralai/mistral-small-3.1-24b-instruct:free`

**Confirmed broken (return HTTP 200 but silently ignore `tool_choice: required`):**
- `stepfun/step-3.5-flash`
- `google/gemma-3-27b`
- `nvidia/nemotron-3-nano-30b`
- `z-ai/glm-4.5-air`

Before adding a new model to config, send a minimal tool_call probe with `tool_choice: required` and verify the response contains a `tool_calls` array, not a plain `content` message.
