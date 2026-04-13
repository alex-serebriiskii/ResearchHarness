using ResearchHarness.Cli.Configuration;

namespace ResearchHarness.Cli.Commands;

public static class CancelCommand
{
    public static Task<int> ExecuteAsync(Guid jobId, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            await client.CancelJobAsync(jobId, ct);
            Console.WriteLine($"Cancellation requested for job {jobId}.");
            return 0;
        }, ct);
}
