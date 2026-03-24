using System.Text;
using System.Text.Json.Nodes;
using ResearchHarness.Core.Models;
using ResearchHarness.Agents.Security;

namespace ResearchHarness.Agents.Prompts;

public static class JournalAssemblyPrompt
{
    public static string BuildSystemPrompt() =>
        PromptSanitizer.SystemPromptPreamble +
        "You are the Institute Lead assembling a research journal. You receive individual research papers and must produce an authoritative overall summary and cross-topic analysis. The summary should be suitable for an executive audience. The cross-topic analysis must identify patterns, contradictions, and knowledge gaps across the research.\n\n" +
        "Format your response using markdown. Use **bold** for key terms and emphasis. Use bullet points or numbered lists to break down multiple items. Separate logical sections with paragraph breaks. Do not use top-level headers (# or ##) — those are reserved for the page layout.\n\n" +
        "For the overall_summary field: Provide a concise executive overview in 2-3 short paragraphs. Lead with the most important finding. Use bullet points for key takeaways.\n\n" +
        "For the cross_topic_analysis field: Structure the analysis with labeled sections using **bold labels** followed by content. Cover: **Patterns**, **Contradictions**, and **Knowledge Gaps**. Use bullet points to enumerate specific items under each.";

    public static string BuildUserMessage(string theme, IEnumerable<Paper> papers)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Research theme: {theme}");
        sb.AppendLine();

        foreach (var paper in papers)
        {
            var sanitizedSummary = PromptSanitizer.SanitizeExternalText(paper.ExecutiveSummary);
            var paperBlock = $"Topic ID: {paper.TopicId}\nExecutive Summary: {sanitizedSummary}\nConfidence Score: {paper.ConfidenceScore}\nFindings: {paper.Findings.Count}";
            sb.AppendLine(PromptSanitizer.WrapUntrustedContent("paper-summary", paperBlock));
            sb.AppendLine();
        }

        sb.Append("Produce: (1) An overall executive summary of the research across all topics. (2) A cross-topic analysis identifying key patterns, contradictions, and gaps. Use markdown formatting: **bold** for emphasis, bullet points for lists, and paragraph breaks between sections.");

        return sb.ToString();
    }

    public static JsonObject BuildOutputSchema() =>
        new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("overall_summary", "cross_topic_analysis"),
            ["properties"] = new JsonObject
            {
                ["overall_summary"] = new JsonObject { ["type"] = "string" },
                ["cross_topic_analysis"] = new JsonObject { ["type"] = "string" }
            }
        };
}
