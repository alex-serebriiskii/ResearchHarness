using System.Text.Json;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Cli.Rendering;

namespace ResearchHarness.Cli.Commands;

public static class JournalCommand
{
    public static Task<int> ExecuteAsync(Guid jobId, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            var journal = await client.GetJournalAsync(jobId, ct);

            var output = config.Format.ToLowerInvariant() switch
            {
                "json" => JsonSerializer.Serialize(journal, CliJsonOptions.Indented),
                "text" => JournalTextRenderer.Render(journal),
                _ => JournalMarkdownRenderer.Render(journal)
            };

            CommandRunner.WriteOutput(config, output);
            return 0;
        }, ct);
}
