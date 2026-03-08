using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Infrastructure.Llm;

/// <summary>
/// ILlmClient implementation backed by OpenRouter's OpenAI-compatible
/// /v1/chat/completions endpoint. Structured output is obtained via
/// OpenAI-style function calling: a single "respond" tool whose parameters
/// schema is the caller-supplied OutputSchema, with tool_choice forced to
/// that function. This is the widest-supported path across the models
/// available on OpenRouter.
/// </summary>
public sealed class OpenRouterLlmClient : ILlmClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RateLimitedExecutor _rateLimiter;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterLlmClient> _logger;

    private static readonly JsonSerializerOptions CamelOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Used when deserializing the caller's type T from model-produced JSON.
    // SnakeCaseLower converts PascalCase C# property names (e.g. ExecutiveSummary) to
    // snake_case (executive_summary) for matching against model-emitted JSON keys.
    // PropertyNameCaseInsensitive adds case-insensitive fallback for single-word fields.
    private static readonly JsonSerializerOptions UserDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // ── Private OpenAI-compatible DTOs ────────────────────────────────────────

    private sealed record OaiMessage(string Role, string Content);

    private sealed record OaiFunctionDef(string Name, string Description, JsonObject Parameters);

    private sealed record OaiTool(string Type, OaiFunctionDef Function);

    private sealed record OaiFunctionRef(string Name);

    private sealed record OaiToolChoice(string Type, OaiFunctionRef Function);

    private sealed record OaiRequest(
        string Model,
        List<OaiMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        double Temperature,
        List<OaiTool>? Tools,
        [property: JsonPropertyName("tool_choice")] OaiToolChoice? ToolChoice);

    private sealed record OaiFunctionCall(string Name, string Arguments);

    private sealed record OaiToolCall(string Type, OaiFunctionCall Function);

    private sealed record OaiMessageResponse(
        string? Role,
        string? Content,
        [property: JsonPropertyName("tool_calls")] List<OaiToolCall>? ToolCalls);

    private sealed record OaiChoice(OaiMessageResponse Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OaiUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

    private sealed record OaiResponse(List<OaiChoice>? Choices, OaiUsage? Usage);

    // ── Constructor ───────────────────────────────────────────────────────────

    public OpenRouterLlmClient(
        IHttpClientFactory httpClientFactory,
        RateLimitedExecutor rateLimiter,
        IOptions<OpenRouterOptions> options,
        ILogger<OpenRouterLlmClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
    }

    // ── ILlmClient ────────────────────────────────────────────────────────────

    public async Task<LlmResponse<T>> CompleteAsync<T>(LlmRequest request, CancellationToken ct = default)
    {
        return await _rateLimiter.ExecuteLlmCallAsync(async () =>
        {
            var oaiRequest = BuildRequest(request, useToolUse: request.OutputSchema != null);
            var response = await SendAsync(oaiRequest, ct);

            var usage = response.Usage ?? new OaiUsage(0, 0);
            _logger.LogInformation(
                "LLM call completed: {InputTokens} in, {OutputTokens} out, model {Model}",
                usage.PromptTokens, usage.CompletionTokens, request.Model);

            var tokenUsage = new TokenUsage(usage.PromptTokens, usage.CompletionTokens);
            var choice = response.Choices?.FirstOrDefault()
                ?? throw new LlmException("OpenRouter returned a response with no choices.");
            var finishReason = choice.FinishReason ?? "";

            if (request.OutputSchema != null)
            {
                // Structured output via function calling: locate the tool_calls block.
                var toolCall = choice.Message.ToolCalls?.FirstOrDefault();

                if (toolCall?.Function?.Arguments is null)
                    throw new LlmException("OpenRouter response did not contain a tool_calls function call with arguments.");

                var argumentsJson = toolCall.Function.Arguments;
                argumentsJson = LlmJsonRepair.RepairStringifiedJsonFields(argumentsJson);
                T? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<T>(argumentsJson, UserDeserializeOptions);
                }
                catch (JsonException ex)
                {
                    throw new LlmException(
                        $"Failed to deserialize function arguments as {typeof(T).Name}: {ex.Message}. Raw: {argumentsJson}");
                }

                if (deserialized is null)
                    throw new LlmException($"Deserialization of function arguments returned null. Raw: {argumentsJson}");

                return new LlmResponse<T>(deserialized, tokenUsage, finishReason);
            }
            else
            {
                var text = choice.Message.Content ?? "";

                // Fast path: T is string — return as-is without round-tripping through JSON.
                if (typeof(T) == typeof(string))
                    return new LlmResponse<T>((T)(object)text, tokenUsage, finishReason);

                T? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<T>(text, UserDeserializeOptions);
                }
                catch (JsonException ex)
                {
                    throw new LlmException(
                        $"Failed to deserialize text response as {typeof(T).Name}: {ex.Message}. Raw: {text}");
                }

                if (deserialized is null)
                    throw new LlmException($"Deserialization of text response returned null. Raw: {text}");

                return new LlmResponse<T>(deserialized, tokenUsage, finishReason);
            }
        }, ct);
    }

    public async Task<LlmResponse<string>> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        return await _rateLimiter.ExecuteLlmCallAsync(async () =>
        {
            var oaiRequest = BuildRequest(request, useToolUse: false);
            var response = await SendAsync(oaiRequest, ct);

            var usage = response.Usage ?? new OaiUsage(0, 0);
            _logger.LogInformation(
                "LLM call completed: {InputTokens} in, {OutputTokens} out, model {Model}",
                usage.PromptTokens, usage.CompletionTokens, request.Model);

            var choice = response.Choices?.FirstOrDefault()
                ?? throw new LlmException("OpenRouter returned a response with no choices.");

            var text = choice.Message.Content ?? "";
            var tokenUsage = new TokenUsage(usage.PromptTokens, usage.CompletionTokens);

            return new LlmResponse<string>(text, tokenUsage, choice.FinishReason ?? "");
        }, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static OaiRequest BuildRequest(LlmRequest request, bool useToolUse)
    {
        // OpenAI-compatible format: system prompt is the first message with role "system".
        var messages = new List<OaiMessage>(request.Messages.Count + 1)
        {
            new OaiMessage("system", request.SystemPrompt)
        };
        messages.AddRange(request.Messages.Select(m => new OaiMessage(m.Role, m.Content)));

        List<OaiTool>? tools = null;
        OaiToolChoice? toolChoice = null;

        if (useToolUse && request.OutputSchema != null)
        {
            tools =
            [
                new OaiTool(
                    "function",
                    new OaiFunctionDef("respond", "Respond with structured data", request.OutputSchema))
            ];
            toolChoice = new OaiToolChoice("function", new OaiFunctionRef("respond"));
        }

        return new OaiRequest(
            request.Model,
            messages,
            request.MaxTokens,
            request.Temperature,
            tools,
            toolChoice);
    }

    private async Task<OaiResponse> SendAsync(OaiRequest requestBody, CancellationToken ct)
    {
        // Adaptive per-request timeout: ~20 tokens/sec output + 30s overhead, capped at 300s
        var timeoutSeconds = Math.Min(requestBody.MaxTokens / 20.0 + 30.0, 300.0);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var requestCt = linkedCts.Token;

        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v1/chat/completions";

        using var httpClient = _httpClientFactory.CreateClient("OpenRouter");

        var currentModel = requestBody.Model;
        var usedFallback = false;

        for (int attempt = 0; ; attempt++)
        {
            var currentRequest = currentModel != requestBody.Model
                ? requestBody with { Model = currentModel }
                : requestBody;
            var json = JsonSerializer.Serialize(currentRequest, CamelOptions);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
            if (!string.IsNullOrEmpty(_options.SiteUrl))
                httpRequest.Headers.Add("HTTP-Referer", _options.SiteUrl);
            if (!string.IsNullOrEmpty(_options.SiteName))
                httpRequest.Headers.Add("X-Title", _options.SiteName);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpResponse = await httpClient.SendAsync(httpRequest, requestCt);

            if (httpResponse.IsSuccessStatusCode)
            {
                var responseJson = await httpResponse.Content.ReadAsStringAsync(requestCt);
                return JsonSerializer.Deserialize<OaiResponse>(responseJson, CamelOptions)
                    ?? throw new LlmException("OpenRouter returned an empty or null response body.");
            }

            var statusCode = (int)httpResponse.StatusCode;
            bool retryable = statusCode == 429 || statusCode >= 500;

            if (retryable && attempt < _options.MaxRetries)
            {
                if (statusCode == 429 && !usedFallback
                    && httpResponse.Headers.RetryAfter is null
                    && _options.FallbackModels.TryGetValue(requestBody.Model, out var fallbackModel))
                {
                    _logger.LogWarning(
                        "OpenRouter 429 for model {Model}; retrying with fallback model {Fallback}",
                        requestBody.Model, fallbackModel);
                    currentModel = fallbackModel;
                    usedFallback = true;
                    continue; // immediate retry with fallback, no delay
                }

                var delaySeconds = statusCode == 429
                    ? GetRateLimitDelay(httpResponse.Headers.RetryAfter, attempt, _options.RateLimitRetryBaseDelaySeconds)
                    : Math.Min(Math.Pow(2, attempt), 30.0);
                _logger.LogWarning(
                    "OpenRouter API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    statusCode, delaySeconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), requestCt);
                continue;
            }

            var errorBody = await httpResponse.Content.ReadAsStringAsync(requestCt);
            throw new LlmException($"OpenRouter API error: HTTP {statusCode}", statusCode, errorBody);
        }
    }

    /// <summary>
    /// Computes the delay to use before retrying a 429 response.
    /// Respects the Retry-After header when provided; otherwise applies a
    /// linear back-off with the configured base, capped at 120 s.
    /// </summary>
    private static double GetRateLimitDelay(
        RetryConditionHeaderValue? retryAfter, int attempt, double baseDelaySeconds)
    {
        double serverDelay = 0;
        if (retryAfter?.Delta is TimeSpan delta)
            serverDelay = delta.TotalSeconds;
        else if (retryAfter?.Date is DateTimeOffset date)
            serverDelay = Math.Max((date - DateTimeOffset.UtcNow).TotalSeconds, 0);

        // Grow linearly so sustained rate-limits are given progressively more
        // time to clear without waiting forever on a fresh transient one.
        var minimum = baseDelaySeconds * (attempt + 1);
        return Math.Min(Math.Max(serverDelay, minimum), 120.0);
    }

}
