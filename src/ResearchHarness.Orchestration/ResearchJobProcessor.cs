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
public sealed partial class ResearchJobProcessor : BackgroundService
{
    private readonly ChannelReader<Guid> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobCancellationService _cancellationService;
    private readonly ILogger<ResearchJobProcessor> _logger;

    public ResearchJobProcessor(
        ChannelReader<Guid> queue,
        IServiceScopeFactory scopeFactory,
        IJobCancellationService cancellationService,
        ILogger<ResearchJobProcessor> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _cancellationService = cancellationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogProcessorStarted(_logger);

        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            LogJobDequeued(_logger, jobId);

            var perJobToken = _cancellationService.RegisterJob(jobId);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, perJobToken);

            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider
                .GetRequiredService<IResearchOrchestrator>();

            try
            {
                await orchestrator.RunJobAsync(jobId, linkedCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                LogJobInterruptedByShutdown(_logger, jobId);
                break;
            }
            catch (Exception ex)
            {
                LogJobProcessorError(_logger, ex, jobId);
            }
            finally
            {
                _cancellationService.CompleteJob(jobId);
            }
        }

        LogProcessorStopped(_logger);
    }

    // ── Structured log methods ────────────────────────────────────────────────

    [LoggerMessage(1011, LogLevel.Information, "ResearchJobProcessor started, waiting for jobs")]
    private static partial void LogProcessorStarted(ILogger logger);

    [LoggerMessage(1012, LogLevel.Information, "ResearchJobProcessor dequeued job {JobId}")]
    private static partial void LogJobDequeued(ILogger logger, Guid jobId);

    [LoggerMessage(1013, LogLevel.Warning, "Job {JobId} processing interrupted by shutdown")]
    private static partial void LogJobInterruptedByShutdown(ILogger logger, Guid jobId);

    [LoggerMessage(1014, LogLevel.Error, "ResearchJobProcessor: job {JobId} threw after orchestrator handling")]
    private static partial void LogJobProcessorError(ILogger logger, Exception ex, Guid jobId);

    [LoggerMessage(1015, LogLevel.Information, "ResearchJobProcessor stopped")]
    private static partial void LogProcessorStopped(ILogger logger);
}
