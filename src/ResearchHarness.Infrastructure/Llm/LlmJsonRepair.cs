using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResearchHarness.Infrastructure.Llm;

/// <summary>
/// Pre-deserialization repair for LLM responses where nested JSON is returned
/// as an escaped string instead of inline JSON. Some models (typically smaller 8B
/// models) produce: {"findings": "[{...}]"} instead of {"findings": [{...}]}.
/// This method repairs top-level string fields whose value parses as a JSON array
/// or object, replacing the string node with the parsed JSON in-place.
/// </summary>
internal static class LlmJsonRepair
{
    internal static string RepairStringifiedJsonFields(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return rawJson;

        JsonNode? root;
        try { root = JsonNode.Parse(rawJson); }
        catch (JsonException) { return rawJson; }

        if (root is not JsonObject obj)
            return rawJson;

        var modified = false;
        foreach (var key in obj.Select(kv => kv.Key).ToArray())
        {
            if (obj[key] is not JsonValue val)
                continue;
            if (!val.TryGetValue<string>(out var str))
                continue;

            var trimmed = str.Trim();
            if ((trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
                (trimmed.StartsWith('{') && trimmed.EndsWith('}')))
            {
                try
                {
                    var parsed = JsonNode.Parse(trimmed);
                    if (parsed is not null)
                    {
                        obj[key] = parsed;
                        modified = true;
                    }
                }
                catch (JsonException) { /* not valid JSON — leave as string */ }
            }
        }

        return modified ? obj.ToJsonString() : rawJson;
    }
}
