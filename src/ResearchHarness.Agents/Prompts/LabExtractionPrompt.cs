using System.Text;
using System.Text.Json.Nodes;
using ResearchHarness.Core.Models;
using ResearchHarness.Agents.Security;

namespace ResearchHarness.Agents.Prompts;

public static class LabExtractionPrompt
{
    public static string BuildSystemPrompt() =>
        PromptSanitizer.SystemPromptPreamble +
        "You are a lab research agent. You receive search results and extract structured findings. Do not synthesize or interpret — extract factual information, note sources, and assess credibility objectively. Rate credibility as High (established institutions, peer-reviewed, official), Medium (reputable news, industry reports), Low (blogs, forums, anonymous), or Unknown.";

    public static string BuildUserMessage(SearchTask task, IEnumerable<SearchHit> hits, IEnumerable<PageContent> pages)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Search query: {task.Query}");
        sb.AppendLine($"Extraction instructions: {task.ExtractionInstructions}");
        sb.AppendLine($"Relevance criteria: {task.RelevanceCriteria}");
        sb.AppendLine();

        int i = 1;
        foreach (var hit in hits.Take(10))
        {
            var title = PromptSanitizer.SanitizeExternalText(
                PromptSanitizer.Truncate(hit.Title, PromptSanitizer.MaxTitleLength));
            var snippet = PromptSanitizer.SanitizeExternalText(
                PromptSanitizer.Truncate(hit.Snippet, PromptSanitizer.MaxSnippetLength));
            var hitBlock = $"Source {i}:\nTitle: {title}\nURL: {hit.Url}\nSnippet: {snippet}";
            sb.AppendLine(PromptSanitizer.WrapUntrustedContent("search-hit", hitBlock));
            sb.AppendLine();
            i++;
        }

        var pageList = pages.ToList();
        if (pageList.Count > 0)
        {
            sb.AppendLine("Full page contents:");
            sb.AppendLine();
            foreach (var page in pageList)
            {
                var pageTitle = page.Title is not null
                    ? PromptSanitizer.SanitizeExternalText(page.Title)
                    : null;
                var text = page.RawText.Length > 2000 ? page.RawText[..2000] : page.RawText;
                text = PromptSanitizer.SanitizeExternalText(text);
                var pageBlock = pageTitle is not null
                    ? $"URL: {page.Url}\nTitle: {pageTitle}\n{text}"
                    : $"URL: {page.Url}\n{text}";
                sb.AppendLine(PromptSanitizer.WrapUntrustedContent("page-content", pageBlock));
                sb.AppendLine();
            }
        }

        sb.Append("Extract findings relevant to the query. For each finding, cite the source URL.");

        return sb.ToString();
    }

    public static JsonObject BuildOutputSchema() =>
        new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("findings", "sources"),
            ["properties"] = new JsonObject
            {
                ["findings"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("sub_topic", "summary", "key_points", "source_url", "relevance_score"),
                        ["properties"] = new JsonObject
                        {
                            ["sub_topic"] = new JsonObject { ["type"] = "string" },
                            ["summary"] = new JsonObject { ["type"] = "string" },
                            ["key_points"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" }
                            },
                            ["source_url"] = new JsonObject { ["type"] = "string" },
                            ["relevance_score"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["minimum"] = 0,
                                ["maximum"] = 1
                            }
                        }
                    }
                },
                ["sources"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("url", "title", "credibility", "credibility_rationale"),
                        ["properties"] = new JsonObject
                        {
                            ["url"] = new JsonObject { ["type"] = "string" },
                            ["title"] = new JsonObject { ["type"] = "string" },
                            ["author"] = new JsonObject { ["type"] = "string" },
                            ["credibility"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray("High", "Medium", "Low", "Unknown")
                            },
                            ["credibility_rationale"] = new JsonObject { ["type"] = "string" }
                        }
                    }
                }
            }
        };
}
