using System.Text.Json.Nodes;

namespace ResearchHarness.Agents.Prompts;

public static class LeadDecompositionPrompt
{
    public static string BuildSystemPrompt() =>
        "You are the Institute Lead of a research institute. Your role is to decompose a research theme into discrete, well-scoped research topics that can be investigated independently. Each topic must have clear boundaries, specific search angles, and expected source types. For Phase 1, produce exactly 1 topic. Be precise and academically rigorous.";

    public static string BuildUserMessage(string theme, int maxTopics) =>
        $"""
        Decompose the following research theme into {maxTopics} research topic(s):

        Theme: {theme}

        For each topic, provide:
        - A clear, specific title
        - A scope description (what the topic covers and what it excludes)
        - 3-5 suggested search angles (specific query directions)
        - Expected source types (e.g., academic papers, news articles, regulatory filings, company reports)
        """;

    public static JsonObject BuildOutputSchema() =>
        new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("topics"),
            ["properties"] = new JsonObject
            {
                ["topics"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("title", "scope", "suggested_search_angles", "expected_source_types"),
                        ["properties"] = new JsonObject
                        {
                            ["title"] = new JsonObject { ["type"] = "string" },
                            ["scope"] = new JsonObject { ["type"] = "string" },
                            ["suggested_search_angles"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            },
                            ["expected_source_types"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            }
                        }
                    }
                }
            }
        };
}
