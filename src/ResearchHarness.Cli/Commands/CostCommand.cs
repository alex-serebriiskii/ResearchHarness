using System.Text;
using System.Text.Json;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Cli.Rendering;

namespace ResearchHarness.Cli.Commands;

public static class CostCommand
{
    public static Task<int> ExecuteAsync(Guid jobId, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            var cost = await client.GetCostAsync(jobId, ct);

            if (config.Format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                CommandRunner.WriteOutput(config, JsonSerializer.Serialize(cost, CliJsonOptions.Indented));
                return 0;
            }

            if (config.Format.Equals("markdown", StringComparison.OrdinalIgnoreCase))
            {
                CommandRunner.WriteOutput(config, RenderMarkdown(cost));
                return 0;
            }

            // Text format
            var sb = new StringBuilder();
            sb.AppendLine($"Total LLM calls: {cost.TotalLlmCalls}");
            sb.AppendLine($"Total input tokens: {cost.TotalInputTokens:N0}");
            sb.AppendLine($"Total output tokens: {cost.TotalOutputTokens:N0}");

            if (cost.ByModel is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("By model:");
                foreach (var (model, usage) in cost.ByModel)
                {
                    sb.AppendLine($"  {model}: {usage.Calls} calls, {usage.InputTokens:N0} in / {usage.OutputTokens:N0} out");
                }
            }

            CommandRunner.WriteOutput(config, sb.ToString());
            return 0;
        }, ct);

    private static string RenderMarkdown(Core.Models.JobCostSummary cost)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Cost Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total LLM calls | {cost.TotalLlmCalls} |");
        sb.AppendLine($"| Total input tokens | {cost.TotalInputTokens:N0} |");
        sb.AppendLine($"| Total output tokens | {cost.TotalOutputTokens:N0} |");

        if (cost.ByModel is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## By Model");
            sb.AppendLine();
            sb.AppendLine("| Model | Calls | Input Tokens | Output Tokens |");
            sb.AppendLine("|-------|-------|-------------|---------------|");
            foreach (var (model, usage) in cost.ByModel)
            {
                sb.AppendLine($"| {model} | {usage.Calls} | {usage.InputTokens:N0} | {usage.OutputTokens:N0} |");
            }
        }

        return sb.ToString();
    }
}
