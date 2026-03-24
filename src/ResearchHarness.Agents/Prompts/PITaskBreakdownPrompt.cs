using System.Text.Json.Nodes;
using ResearchHarness.Core.Models;
using ResearchHarness.Agents.Security;

namespace ResearchHarness.Agents.Prompts;

public static class PITaskBreakdownPrompt
{
    public static string BuildSystemPrompt() =>
        PromptSanitizer.SystemPromptPreamble +
        "You are a Principal Investigator at a research institute. Given a research topic, you create a precise set of search tasks for lab agents to execute. Each task must have a specific, searchable query, target source types, clear extraction instructions, and relevance criteria. Prioritize specificity — vague queries produce poor results.";

    public static string BuildUserMessage(ResearchTopic topic, int maxTasks) =>
        $"""
        Create up to {maxTasks} search tasks for the following research topic:

        Title: {topic.Title}
        Scope: {topic.Scope}
        Suggested search angles:
        {string.Join("\n", topic.SuggestedSearchAngles.Select(a => $"- {a}"))}

        Expected source types: {string.Join(", ", topic.ExpectedSourceTypes)}

        For each task, specify:
        - A precise search query (ready to submit to a search engine)
        - Target source types for this specific query
        - Extraction instructions (exactly what information to extract)
        - Relevance criteria (what makes a result relevant vs. irrelevant)
        - Whether to fetch full page content (only for critical sources)
        """;

    public static JsonObject BuildOutputSchema() =>
        new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("tasks"),
            ["properties"] = new JsonObject
            {
                ["tasks"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("query", "target_source_types", "extraction_instructions", "relevance_criteria"),
                        ["properties"] = new JsonObject
                        {
                            ["query"] = new JsonObject { ["type"] = "string" },
                            ["target_source_types"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            },
                            ["extraction_instructions"] = new JsonObject { ["type"] = "string" },
                            ["relevance_criteria"] = new JsonObject { ["type"] = "string" },
                            ["fetch_page_content"] = new JsonObject { ["type"] = "boolean" }
                        }
                    }
                }
            }
        };
}
