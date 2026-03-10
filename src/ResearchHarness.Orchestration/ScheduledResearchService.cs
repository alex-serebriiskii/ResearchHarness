using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;
using ResearchHarness.Core.Interfaces;

namespace ResearchHarness.Orchestration;

/// <summary>
/// Background service that triggers research jobs on a cron schedule.
/// Schedule is read from configuration at startup.
/// </summary>
public sealed partial class ScheduledResearchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledResearchService> _logger;
    private readonly List<ScheduleEntry> _entries;

    private sealed class ScheduleEntry
    {
        public string Theme { get; init; } = "";
        public CrontabSchedule Schedule { get; init; } = null!;
        public DateTime LastTriggered { get; set; } = DateTime.MinValue;
    }

    public ScheduledResearchService(
        IServiceScopeFactory scopeFactory,
        IOptions<ResearchScheduleOptions> options,
        ILogger<ScheduledResearchService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _entries = options.Value.Schedule
            .Where(e => !string.IsNullOrWhiteSpace(e.Theme) && !string.IsNullOrWhiteSpace(e.CronExpression))
            .Select(e => new ScheduleEntry
            {
                Theme = e.Theme,
                Schedule = CrontabSchedule.Parse(e.CronExpression)
            })
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_entries.Count == 0)
        {
            LogNoSchedules(_logger);
            return;
        }

        LogSchedulerStarted(_logger, _entries.Count);

        // Initialize LastTriggered so we don't immediately fire on startup
        var now = DateTime.UtcNow;
        foreach (var entry in _entries)
            entry.LastTriggered = now;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            now = DateTime.UtcNow;
            foreach (var entry in _entries)
            {
                var next = entry.Schedule.GetNextOccurrence(entry.LastTriggered);
                if (next <= now)
                {
                    entry.LastTriggered = now;
                    LogTriggeringJob(_logger, entry.Theme);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var orchestrator = scope.ServiceProvider
                            .GetRequiredService<IResearchOrchestrator>();
                        await orchestrator.StartResearchAsync(entry.Theme, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        LogScheduledJobFailed(_logger, ex, entry.Theme);
                    }
                }
            }
        }
    }

    [LoggerMessage(1020, LogLevel.Information, "Scheduled research service: no schedules configured, exiting")]
    private static partial void LogNoSchedules(ILogger logger);

    [LoggerMessage(1021, LogLevel.Information, "Scheduled research service started with {Count} schedule(s)")]
    private static partial void LogSchedulerStarted(ILogger logger, int count);

    [LoggerMessage(1022, LogLevel.Information, "Triggering scheduled research job for theme: {Theme}")]
    private static partial void LogTriggeringJob(ILogger logger, string theme);

    [LoggerMessage(1023, LogLevel.Error, "Scheduled research job failed for theme: {Theme}")]
    private static partial void LogScheduledJobFailed(ILogger logger, Exception ex, string theme);
}
