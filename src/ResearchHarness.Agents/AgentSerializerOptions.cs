using System.Text.Json;

namespace ResearchHarness.Agents;

internal static class AgentSerializerOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}
