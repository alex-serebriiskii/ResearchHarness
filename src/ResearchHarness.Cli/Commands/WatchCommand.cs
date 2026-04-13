using System.Text.Json;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Cli.Rendering;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Commands;

public static class WatchCommand
{
    public static Task<int> ExecuteAsync(Guid jobId, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            // Check current status — the job may already be done
            var currentStatus = await client.GetStatusAsync(jobId, ct);
            if (currentStatus is null)
            {
                CommandRunner.WriteError($"Job {jobId} not found.");
                return 2;
            }

            if (currentStatus is JobStatus.Completed)
                return await RenderJournal(client, jobId, config, ct);

            if (currentStatus is JobStatus.Failed)
            {
                CommandRunner.WriteError("Research job already failed.");
                return 1;
            }

            CommandRunner.WriteProgress(config, $"Watching job {jobId} (currently {currentStatus})...");

            JobStatus finalStatus;
            try
            {
                finalStatus = await JobPoller.PollToCompletionAsync(client, jobId, config, ct);
            }
            catch (TimeoutException ex)
            {
                CommandRunner.WriteError(ex.Message);
                return 2;
            }

            if (finalStatus == JobStatus.Failed)
            {
                CommandRunner.WriteError("Research job failed.");
                return 1;
            }

            return await RenderJournal(client, jobId, config, ct);
        }, ct);

    private static async Task<int> RenderJournal(
        Client.IResearchHarnessClient client, Guid jobId, CliConfiguration config, CancellationToken ct)
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
    }
}
