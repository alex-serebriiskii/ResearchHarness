using System.Text.Json.Nodes;

namespace ResearchHarness.Core.Llm;

/// <summary>
/// A request to the LLM. OutputSchema, when provided, instructs the Anthropic API
/// to enforce structured output (JSON schema), eliminating regex-based parsing.
/// </summary>
public record LlmRequest(
    string Model,
    string SystemPrompt,
    List<LlmMessage> Messages,
    JsonObject? OutputSchema = null,
    int MaxTokens = 4096,
    double Temperature = 0.3
);

public record LlmMessage(string Role, string Content)
{
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string content) => new("assistant", content);
}
