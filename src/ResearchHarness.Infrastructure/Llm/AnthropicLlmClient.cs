using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Infrastructure.Llm;

public sealed class AnthropicLlmClient : ILlmClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RateLimitedExecutor _rateLimiter;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicLlmClient> _logger;

    private static readonly JsonSerializerOptions SnakeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Private Anthropic API DTOs ────────────────────────────────────────────

    private sealed record AnthropicMessage(string Role, string Content);

    private sealed record AnthropicTool(
        string Name,
        string Description,
        JsonObject InputSchema);

    private sealed record AnthropicToolChoice(string Type, string Name);

    private sealed record AnthropicRequest(
        string Model,
        int MaxTokens,
        string System,
        List<AnthropicMessage> Messages,
        double Temperature,
        List<AnthropicTool>? Tools,
        AnthropicToolChoice? ToolChoice);

    private sealed record AnthropicContentBlock(
        string Type,
        string? Text,
        JsonObject? Input);

    private sealed record AnthropicUsage(int InputTokens, int OutputTokens);

    private sealed record AnthropicResponse(
        List<AnthropicContentBlock>? Content,
        AnthropicUsage? Usage,
        string? StopReason);

    // ── Constructor ───────────────────────────────────────────────────────────

    public AnthropicLlmClient(
        IHttpClientFactory httpClientFactory,
        RateLimitedExecutor rateLimiter,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicLlmClient> logger)
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
            var anthropicRequest = BuildRequest(request, useToolUse: request.OutputSchema != null);
            var response = await SendAsync(anthropicRequest, ct);

            var usage = response.Usage ?? new AnthropicUsage(0, 0);
            _logger.LogInformation(
                "LLM call completed: {InputTokens} in, {OutputTokens} out, model {Model}",
                usage.InputTokens, usage.OutputTokens, request.Model);

            var tokenUsage = new TokenUsage(usage.InputTokens, usage.OutputTokens);
            var stopReason = response.StopReason ?? "";

            if (request.OutputSchema != null)
            {
                // Structured output via tool_use: locate the tool_use content block.
                var toolBlock = response.Content?.FirstOrDefault(b => b.Type == "tool_use");

                if (toolBlock?.Input is null)
                    throw new LlmException("Anthropic response did not contain a tool_use content block with input.");

                var inputJson = toolBlock.Input.ToJsonString();
                inputJson = LlmJsonRepair.RepairStringifiedJsonFields(inputJson);
                T? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<T>(inputJson);
                }
                catch (JsonException ex)
                {
                    throw new LlmException(
                        $"Failed to deserialize tool_use input as {typeof(T).Name}: {ex.Message}. Raw: {inputJson}");
                }

                if (deserialized is null)
                    throw new LlmException($"Deserialization of tool_use input returned null. Raw: {inputJson}");

                return new LlmResponse<T>(deserialized, tokenUsage, stopReason);
            }
            else
            {
                // Text completion: extract text block.
                var textBlock = response.Content?.FirstOrDefault(b => b.Type == "text");
                var text = textBlock?.Text ?? "";

                // Fast path: T is string — return as-is without round-tripping through JSON.
                if (typeof(T) == typeof(string))
                    return new LlmResponse<T>((T)(object)text, tokenUsage, stopReason);

                T? deserialized;
                try
                {
                    deserialized = JsonSerializer.Deserialize<T>(text);
                }
                catch (JsonException ex)
                {
                    throw new LlmException(
                        $"Failed to deserialize text response as {typeof(T).Name}: {ex.Message}. Raw: {text}");
                }

                if (deserialized is null)
                    throw new LlmException($"Deserialization of text response returned null. Raw: {text}");

                return new LlmResponse<T>(deserialized, tokenUsage, stopReason);
            }
        }, ct);
    }

    public async Task<LlmResponse<string>> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        return await _rateLimiter.ExecuteLlmCallAsync(async () =>
        {
            var anthropicRequest = BuildRequest(request, useToolUse: false);
            var response = await SendAsync(anthropicRequest, ct);

            var usage = response.Usage ?? new AnthropicUsage(0, 0);
            _logger.LogInformation(
                "LLM call completed: {InputTokens} in, {OutputTokens} out, model {Model}",
                usage.InputTokens, usage.OutputTokens, request.Model);

            var textBlock = response.Content?.FirstOrDefault(b => b.Type == "text");
            var text = textBlock?.Text ?? "";
            var tokenUsage = new TokenUsage(usage.InputTokens, usage.OutputTokens);

            return new LlmResponse<string>(text, tokenUsage, response.StopReason ?? "");
        }, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AnthropicRequest BuildRequest(LlmRequest request, bool useToolUse)
    {
        var messages = request.Messages
            .Select(m => new AnthropicMessage(m.Role, m.Content))
            .ToList();

        List<AnthropicTool>? tools = null;
        AnthropicToolChoice? toolChoice = null;

        if (useToolUse && request.OutputSchema != null)
        {
            tools = [new AnthropicTool("respond", "Respond with structured data", request.OutputSchema)];
            toolChoice = new AnthropicToolChoice("tool", "respond");
        }

        return new AnthropicRequest(
            request.Model,
            request.MaxTokens,
            request.SystemPrompt,
            messages,
            request.Temperature,
            tools,
            toolChoice);
    }

    private async Task<AnthropicResponse> SendAsync(AnthropicRequest requestBody, CancellationToken ct)
    {
        // Adaptive per-request timeout: ~20 tokens/sec output + 30s overhead, capped at 300s
        var timeoutSeconds = Math.Min(requestBody.MaxTokens / 20.0 + 30.0, 300.0);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var requestCt = linkedCts.Token;

        var json = JsonSerializer.Serialize(requestBody, SnakeOptions);
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v1/messages";

        using var httpClient = _httpClientFactory.CreateClient("Anthropic");

        for (int attempt = 0; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("x-api-key", _options.ApiKey);
            httpRequest.Headers.Add("anthropic-version", _options.Version);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpResponse = await httpClient.SendAsync(httpRequest, requestCt);

            if (httpResponse.IsSuccessStatusCode)
            {
                var responseJson = await httpResponse.Content.ReadAsStringAsync(requestCt);
                return JsonSerializer.Deserialize<AnthropicResponse>(responseJson, SnakeOptions)
                    ?? throw new LlmException("Anthropic returned an empty or null response body.");
            }

            var statusCode = (int)httpResponse.StatusCode;
            bool retryable = statusCode == 429 || statusCode >= 500;

            if (retryable && attempt < _options.MaxRetries)
            {
                var delaySeconds = Math.Min(Math.Pow(2, attempt), 30.0);
                _logger.LogWarning(
                    "Anthropic API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    statusCode, delaySeconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), requestCt);
                continue;
            }

            var errorBody = await httpResponse.Content.ReadAsStringAsync(requestCt);
            throw new LlmException($"Anthropic API error: HTTP {statusCode}", statusCode, errorBody);
        }
    }
}
