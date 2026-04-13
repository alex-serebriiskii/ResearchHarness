using ResearchHarness.Cli.Configuration;
using ResearchHarness.Client;

namespace ResearchHarness.Cli.Commands;

public static class CommandRunner
{
    public static async Task<int> ExecuteAsync(
        CliConfiguration config,
        Func<IResearchHarnessClient, CancellationToken, Task<int>> action,
        CancellationToken ct = default)
    {
        HttpMessageHandler? handler = config.Verbose
            ? new VerboseLoggingHandler(new HttpClientHandler())
            : null;

        await using var client = new ResearchHarnessClient(
            config.Server, config.ApiKey, httpTimeout: TimeSpan.FromSeconds(30), innerHandler: handler);

        try
        {
            return await action(client, ct);
        }
        catch (OperationCanceledException)
        {
            WriteError("Operation cancelled.");
            return 1;
        }
        catch (JobNotFoundException ex)
        {
            WriteError($"Job {ex.JobId} not found.");
            return 2;
        }
        catch (JobNotReadyException ex)
        {
            WriteError(ex.ResponseBody ?? "Job is not yet completed.");
            return 2;
        }
        catch (HttpRequestException ex)
        {
            WriteError($"Cannot connect to {config.Server}. ({ex.Message})");
            return 2;
        }
        catch (ResearchHarnessApiException ex)
        {
            WriteError(ex.Message);
            return 2;
        }
    }

    public static void WriteError(string message) =>
        Console.Error.WriteLine($"Error: {message}");

    public static void WriteProgress(CliConfiguration config, string message)
    {
        if (!config.NoProgress)
            Console.Error.WriteLine(message);
    }

    public static void WriteOutput(CliConfiguration config, string content)
    {
        if (config.OutputFile is not null)
            File.WriteAllText(config.OutputFile, content);
        else
            Console.Write(content);
    }
}
