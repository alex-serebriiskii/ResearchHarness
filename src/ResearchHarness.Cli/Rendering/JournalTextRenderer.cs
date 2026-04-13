using System.Text;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Rendering;

public static class JournalTextRenderer
{
    public static string Render(Journal journal)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== OVERALL SUMMARY ===");
        sb.AppendLine(journal.OverallSummary);
        sb.AppendLine();

        sb.AppendLine("=== CROSS-TOPIC ANALYSIS ===");
        sb.AppendLine(journal.CrossTopicAnalysis);
        sb.AppendLine();

        if (journal.Papers is { Count: > 0 })
        {
            for (var i = 0; i < journal.Papers.Count; i++)
            {
                var paper = journal.Papers[i];
                sb.AppendLine($"=== PAPER {i + 1} ===");
                sb.AppendLine($"Confidence: {paper.ConfidenceScore:P0} | Revisions: {paper.RevisionCount}");
                sb.AppendLine();
                sb.AppendLine(paper.ExecutiveSummary);
                sb.AppendLine();

                if (paper.Findings is { Count: > 0 })
                {
                    for (var j = 0; j < paper.Findings.Count; j++)
                    {
                        var f = paper.Findings[j];
                        sb.AppendLine($"  {j + 1}. [{f.SubTopic}] {f.Summary}");
                        if (f.KeyPoints is { Count: > 0 })
                        {
                            foreach (var point in f.KeyPoints)
                                sb.AppendLine($"     - {point}");
                        }
                    }
                    sb.AppendLine();
                }
            }
        }

        if (journal.MasterBibliography is { Count: > 0 })
        {
            sb.AppendLine($"=== BIBLIOGRAPHY ({journal.MasterBibliography.Count} sources) ===");
            foreach (var source in journal.MasterBibliography)
            {
                sb.AppendLine($"  [{source.Credibility}] {source.Title} | {source.Url}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Assembled at: {journal.AssembledAt:u}");

        return sb.ToString();
    }
}
