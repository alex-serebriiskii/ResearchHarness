using ResearchHarness.Cli.Configuration;

namespace ResearchHarness.Cli.Commands;

public static class SubmitCommand
{
    public static Task<int> ExecuteAsync(string theme, CliConfiguration config, CancellationToken ct) =>
        CommandRunner.ExecuteAsync(config, async (client, ct) =>
        {
            var jobId = await client.StartJobAsync(theme, ct);
            Console.WriteLine(jobId);
            return 0;
        }, ct);
}
