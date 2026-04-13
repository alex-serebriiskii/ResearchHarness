using System.Text;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Rendering;

public static class JournalMarkdownRenderer
{
    public static string Render(Journal journal)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Research Journal");
        sb.AppendLine();
        sb.AppendLine("## Overall Summary");
        sb.AppendLine();
        sb.AppendLine(journal.OverallSummary);
        sb.AppendLine();

        sb.AppendLine("## Cross-Topic Analysis");
        sb.AppendLine();
        sb.AppendLine(journal.CrossTopicAnalysis);
        sb.AppendLine();

        if (journal.Papers is { Count: > 0 })
        {
            sb.AppendLine("---");
            sb.AppendLine();

            for (var i = 0; i < journal.Papers.Count; i++)
            {
                var paper = journal.Papers[i];
                RenderPaper(sb, paper, i + 1);
            }
        }

        if (journal.MasterBibliography is { Count: > 0 })
        {
            sb.AppendLine("## Master Bibliography");
            sb.AppendLine();
            RenderBibliography(sb, journal.MasterBibliography);
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Assembled at: {journal.AssembledAt:u}*");

        return sb.ToString();
    }

    private static void RenderPaper(StringBuilder sb, Paper paper, int index)
    {
        sb.AppendLine($"## Paper {index}");
        sb.AppendLine();

        sb.Append($"**Confidence:** {paper.ConfidenceScore:P0}");
        sb.AppendLine($" | **Revisions:** {paper.RevisionCount}");
        sb.AppendLine();

        if (paper.Reviews is { Count: > 0 })
        {
            var verdicts = string.Join(", ", paper.Reviews.Select(r => r.Verdict));
            sb.AppendLine($"**Review verdicts:** {verdicts}");
            sb.AppendLine();
        }

        sb.AppendLine("### Executive Summary");
        sb.AppendLine();
        sb.AppendLine(paper.ExecutiveSummary);
        sb.AppendLine();

        if (paper.Findings is { Count: > 0 })
        {
            sb.AppendLine("### Findings");
            sb.AppendLine();

            for (var j = 0; j < paper.Findings.Count; j++)
            {
                var finding = paper.Findings[j];
                sb.AppendLine($"#### {j + 1}. {finding.SubTopic}");
                sb.AppendLine();
                sb.AppendLine(finding.Summary);
                sb.AppendLine();

                if (finding.KeyPoints is { Count: > 0 })
                {
                    foreach (var point in finding.KeyPoints)
                    {
                        sb.AppendLine($"- {point}");
                    }
                    sb.AppendLine();
                }
            }
        }

        if (paper.Bibliography is { Count: > 0 })
        {
            sb.AppendLine("### Bibliography");
            sb.AppendLine();
            RenderBibliography(sb, paper.Bibliography);
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void RenderBibliography(StringBuilder sb, List<Source> sources)
    {
        sb.AppendLine("| # | Title | Credibility | URL |");
        sb.AppendLine("|---|-------|-------------|-----|");

        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            var title = s.Title?.Replace("|", "\\|") ?? "Untitled";
            var url = s.Url ?? "";
            sb.AppendLine($"| {i + 1} | {title} | {s.Credibility} | {url} |");
        }

        sb.AppendLine();
    }
}
