using ResearchHarness.Cli.Configuration;

namespace ResearchHarness.Cli.Commands;

public static class StatusCommand
{
    public static Task<int> ExecuteAsync(Guid jobId, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            var status = await client.GetStatusAsync(jobId, ct);
            if (status is null)
            {
                CommandRunner.WriteError($"Job {jobId} not found.");
                return 2;
            }

            Console.WriteLine(status);
            return 0;
        }, ct);
}
