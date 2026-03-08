using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResearchHarness.Core.Interfaces;

namespace ResearchHarness.Orchestration;

/// <summary>
/// Background service that drains the job queue and executes each job via a
/// scoped IResearchOrchestrator. One job runs at a time in Phase 1.
/// Phase 2+: add a SemaphoreSlim to bound parallel job execution.
/// </summary>
public sealed class ResearchJobProcessor : BackgroundService
{
    private readonly ChannelReader<Guid> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResearchJobProcessor> _logger;

    public ResearchJobProcessor(
        ChannelReader<Guid> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ResearchJobProcessor> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ResearchJobProcessor started, waiting for jobs");

        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation("ResearchJobProcessor dequeued job {JobId}", jobId);

            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider
                .GetRequiredService<IResearchOrchestrator>();

            try
            {
                await orchestrator.RunJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Job {JobId} processing interrupted by shutdown", jobId);
                break;
            }
            catch (Exception ex)
            {
                // Orchestrator already marked the job as Failed and logged the error.
                // We catch here to prevent the processor from dying on a single job failure.
                _logger.LogError(ex,
                    "ResearchJobProcessor: job {JobId} threw after orchestrator handling", jobId);
            }
        }

        _logger.LogInformation("ResearchJobProcessor stopped");
    }
}
