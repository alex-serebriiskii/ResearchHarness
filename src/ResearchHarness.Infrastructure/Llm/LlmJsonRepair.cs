using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResearchHarness.Infrastructure.Llm;

/// <summary>
/// Pre-deserialization repair for LLM responses where nested JSON is returned
/// as an escaped string instead of inline JSON. Some models (typically smaller 8B
/// models) produce: {"findings": "[{...}]"} instead of {"findings": [{...}]}.
/// This method repairs string fields whose value parses as a JSON array
/// or object, replacing the string node with the parsed JSON in-place.
/// It recurses into nested objects and arrays to handle deeply nested cases.
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

        return RepairObject(obj) ? obj.ToJsonString() : rawJson;
    }

    private static bool RepairObject(JsonObject obj)
    {
        var modified = false;
        foreach (var key in obj.Select(kv => kv.Key).ToArray())
        {
            var node = obj[key];
            if (node is JsonValue val && val.TryGetValue<string>(out var str))
            {
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
                            // If we just parsed an object, recurse into it
                            if (parsed is JsonObject nestedObj)
                                RepairObject(nestedObj);
                            else if (parsed is JsonArray nestedArr)
                                modified |= RepairArray(nestedArr);
                        }
                    }
                    catch (JsonException) { /* not valid JSON — leave as string */ }
                }
            }
            else if (node is JsonObject childObj)
            {
                modified |= RepairObject(childObj);
            }
            else if (node is JsonArray childArr)
            {
                modified |= RepairArray(childArr);
            }
        }
        return modified;
    }

    private static bool RepairArray(JsonArray arr)
    {
        var modified = false;
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonObject elemObj)
                modified |= RepairObject(elemObj);
        }
        return modified;
    }
}
