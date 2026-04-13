using ResearchHarness.Cli.Configuration;
using ResearchHarness.Client;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Cli.Commands;

/// <summary>
/// Polls a research job to completion with retry on transient network errors.
/// Returns <see cref="JobStatus.Completed"/> or <see cref="JobStatus.Failed"/>.
/// Throws <see cref="TimeoutException"/> on timeout, <see cref="OperationCanceledException"/> on cancellation,
/// or <see cref="HttpRequestException"/> after exhausting retries.
/// </summary>
public static class JobPoller
{
    private const int MaxConsecutiveErrors = 3;

    public static async Task<JobStatus> PollToCompletionAsync(
        IResearchHarnessClient client,
        Guid jobId,
        CliConfiguration config,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        var consecutiveErrors = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed > timeout)
                throw new TimeoutException(
                    $"Timed out after {config.TimeoutSeconds}s. " +
                    $"Use 'research-harness status {jobId}' to check later.");

            JobStatus? status;
            try
            {
                status = await client.GetStatusAsync(jobId, ct);
                consecutiveErrors = 0;
            }
            catch (HttpRequestException ex) when (consecutiveErrors < MaxConsecutiveErrors)
            {
                consecutiveErrors++;
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, consecutiveErrors));
                CommandRunner.WriteProgress(config,
                    $"Connection error ({ex.Message}), retrying in {backoff.TotalSeconds:F0}s... " +
                    $"({consecutiveErrors}/{MaxConsecutiveErrors})");
                await Task.Delay(backoff, ct);
                continue;
            }

            if (status is null)
                throw new JobNotFoundException(jobId);

            CommandRunner.WriteProgress(config,
                $"[{DateTime.Now:HH:mm:ss}] {status}  elapsed={elapsed.TotalSeconds:F0}s");

            if (status is JobStatus.Completed or JobStatus.Failed)
                return status.Value;

            await Task.Delay(TimeSpan.FromSeconds(config.PollIntervalSeconds), ct);
        }
    }
}
