using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    // Case-insensitive so that snake_case JSON keys (from model output matching the
    // schema) map correctly to PascalCase C# record properties.
    private static readonly JsonSerializerOptions UserDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
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
        var json = JsonSerializer.Serialize(requestBody, CamelOptions);
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v1/chat/completions";

        using var httpClient = _httpClientFactory.CreateClient("OpenRouter");

        for (int attempt = 0; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
            if (!string.IsNullOrEmpty(_options.SiteUrl))
                httpRequest.Headers.Add("HTTP-Referer", _options.SiteUrl);
            if (!string.IsNullOrEmpty(_options.SiteName))
                httpRequest.Headers.Add("X-Title", _options.SiteName);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpResponse = await httpClient.SendAsync(httpRequest, ct);

            if (httpResponse.IsSuccessStatusCode)
            {
                var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<OaiResponse>(responseJson, CamelOptions)
                    ?? throw new LlmException("OpenRouter returned an empty or null response body.");
            }

            var statusCode = (int)httpResponse.StatusCode;
            bool retryable = statusCode == 429 || statusCode >= 500;

            if (retryable && attempt < _options.MaxRetries)
            {
                var delaySeconds = Math.Min(Math.Pow(2, attempt), 30.0);
                _logger.LogWarning(
                    "OpenRouter API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    statusCode, delaySeconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                continue;
            }

            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            throw new LlmException($"OpenRouter API error: HTTP {statusCode}", statusCode, errorBody);
        }
    }
}
