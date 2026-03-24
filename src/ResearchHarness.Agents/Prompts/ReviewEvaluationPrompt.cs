using System.Text;
using System.Text.Json.Nodes;
using ResearchHarness.Core.Models;
using ResearchHarness.Agents.Security;

namespace ResearchHarness.Agents.Prompts;

internal static class ReviewEvaluationPrompt
{
    internal static string BuildSystemPrompt() =>
        PromptSanitizer.SystemPromptPreamble +
        "You are a rigorous peer reviewer evaluating a research paper. " +
        "Assess the paper on: factual consistency (do findings match sources?), " +
        "source quality (are sources credible?), completeness (does it address the topic scope?), " +
        "and logical coherence (are conclusions supported by evidence?). " +
        "Return verdict Accept if the paper meets standards, Revise if improvements are needed, " +
        "or Reject if the paper is fundamentally flawed and cannot be salvaged.";

    internal static string BuildUserMessage(Paper paper, ResearchTopic topic)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Research Topic: {topic.Title}");
        sb.AppendLine($"Scope: {topic.Scope}");
        sb.AppendLine();
        sb.AppendLine(PromptSanitizer.WrapUntrustedContent("paper-content", PromptSanitizer.SanitizeExternalText($"Executive Summary: {paper.ExecutiveSummary}")));
        sb.AppendLine($"Confidence Score: {paper.ConfidenceScore:F2}");
        sb.AppendLine($"Number of Findings: {paper.Findings.Count}");
        sb.AppendLine($"Number of Sources: {paper.Bibliography.Count}");
        if (paper.Findings.Count > 0)
        {
            sb.AppendLine();
            var findingsSb = new StringBuilder();
            findingsSb.AppendLine("Key Findings:");
            foreach (var f in paper.Findings.Take(5))
                findingsSb.AppendLine($"  - [{f.SubTopic}] {f.Summary}");
            sb.AppendLine(PromptSanitizer.WrapUntrustedContent("paper-findings", PromptSanitizer.SanitizeExternalText(findingsSb.ToString())));
        }
        return sb.ToString();
    }

    internal static JsonObject BuildOutputSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("verdict", "feedback"),
            ["properties"] = new JsonObject
            {
                ["verdict"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("Accept", "Revise", "Reject")
                },
                ["feedback"] = new JsonObject { ["type"] = "string" },
                ["issues"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };
}
