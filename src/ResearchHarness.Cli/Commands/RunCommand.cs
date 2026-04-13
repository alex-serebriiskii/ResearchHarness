using System.Text.Json;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Cli.Rendering;
using ResearchHarness.Client;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Commands;

public static class RunCommand
{
    public static Task<int> ExecuteAsync(string theme, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var cancelRequested = false;

            void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                if (cancelRequested) return;
                cancelRequested = true;
                CommandRunner.WriteProgress(config, "Cancelling job...");
                cts.Cancel();
            }

            Console.CancelKeyPress += OnCancelKeyPress;
            try
            {
                var jobId = await client.StartJobAsync(theme, cts.Token);
                CommandRunner.WriteProgress(config, $"Job submitted: {jobId}");

                var startTime = DateTime.UtcNow;

                JobStatus finalStatus;
                try
                {
                    finalStatus = await JobPoller.PollToCompletionAsync(client, jobId, config, cts.Token);
                }
                catch (OperationCanceledException) when (cancelRequested)
                {
                    // Best-effort server-side cancellation before exiting
                    try { await client.CancelJobAsync(jobId); }
                    catch { /* best-effort */ }
                    CommandRunner.WriteError("Job cancelled.");
                    return 1;
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

                // Retrieve and render the journal
                var journal = await client.GetJournalAsync(jobId, cts.Token);
                var output = FormatJournal(journal, config);
                CommandRunner.WriteOutput(config, output);

                // Show cost summary on stderr if available
                try
                {
                    var cost = await client.GetCostAsync(jobId, cts.Token);
                    var elapsed = DateTime.UtcNow - startTime;
                    CommandRunner.WriteProgress(config,
                        $"Completed in {elapsed.TotalSeconds:F0}s | " +
                        $"LLM calls: {cost.TotalLlmCalls} | " +
                        $"Tokens: {cost.TotalInputTokens:N0} in / {cost.TotalOutputTokens:N0} out");
                }
                catch
                {
                    // Cost is optional — don't fail the command if unavailable
                }

                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
            }
        }, ct);

    private static string FormatJournal(Journal journal, CliConfiguration config) =>
        config.Format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(journal, CliJsonOptions.Indented),
            "text" => JournalTextRenderer.Render(journal),
            _ => JournalMarkdownRenderer.Render(journal)
        };
}
