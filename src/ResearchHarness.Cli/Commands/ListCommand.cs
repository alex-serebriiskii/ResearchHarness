using System.Text;
using System.Text.Json;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Cli.Rendering;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Commands;

public static class ListCommand
{
    public static Task<int> ExecuteAsync(
        JobStatus? status,
        int offset,
        int limit,
        CliConfiguration config,
        CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            var result = await client.ListJobsAsync(status, offset, limit, ct);

            if (config.Format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                CommandRunner.WriteOutput(config, JsonSerializer.Serialize(result, CliJsonOptions.Indented));
                return 0;
            }

            if (config.Format.Equals("markdown", StringComparison.OrdinalIgnoreCase))
            {
                CommandRunner.WriteOutput(config, RenderMarkdown(result));
                return 0;
            }

            // Text format
            var sb = new StringBuilder();
            sb.AppendLine($"Jobs ({result.Total} total):");
            sb.AppendLine();

            if (result.Jobs.Count == 0)
            {
                sb.AppendLine("  (none)");
                CommandRunner.WriteOutput(config, sb.ToString());
                return 0;
            }

            foreach (var job in result.Jobs)
            {
                var completed = job.CompletedAt?.ToString("u") ?? "—";
                sb.AppendLine($"  {job.JobId}  {job.Status,-12}  topics={job.TopicCount}  created={job.CreatedAt:u}  completed={completed}");
                sb.AppendLine($"    {job.Theme}");
                sb.AppendLine();
            }

            CommandRunner.WriteOutput(config, sb.ToString());
            return 0;
        }, ct);

    private static string RenderMarkdown(Client.JobListResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Research Jobs ({result.Total} total)");
        sb.AppendLine();

        if (result.Jobs.Count == 0)
        {
            sb.AppendLine("*No jobs found.*");
            return sb.ToString();
        }

        sb.AppendLine("| Job ID | Status | Topics | Created | Completed | Theme |");
        sb.AppendLine("|--------|--------|--------|---------|-----------|-------|");

        foreach (var job in result.Jobs)
        {
            var completed = job.CompletedAt?.ToString("u") ?? "—";
            var theme = job.Theme.Replace("|", "\\|");
            sb.AppendLine($"| `{job.JobId}` | {job.Status} | {job.TopicCount} | {job.CreatedAt:u} | {completed} | {theme} |");
        }

        return sb.ToString();
    }
}
