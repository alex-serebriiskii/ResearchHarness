using System.Collections.Concurrent;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Web.Services;

/// <summary>
/// Singleton event broadcaster that bridges the orchestrator (running in a BackgroundService scope)
/// and Blazor circuits (each in their own DI scope). Blazor components subscribe to the
/// <see cref="OnProgress"/> event and call InvokeAsync(StateHasChanged) in their handler.
/// </summary>
public sealed class JobProgressBroadcaster : IJobProgressNotifier
{
    private readonly ConcurrentDictionary<Guid, List<JobProgressEvent>> _history = new();
    private readonly object _lock = new();

    /// <summary>
    /// Raised on every progress notification. Subscribers MUST marshal to their own
    /// synchronization context (e.g. Blazor's InvokeAsync).
    /// </summary>
    public event Action<JobProgressEvent>? OnProgress;

    public Task NotifyAsync(JobProgressEvent progress, CancellationToken ct = default)
    {
        // Store in history for late-joining subscribers
        var events = _history.GetOrAdd(progress.JobId, _ => new List<JobProgressEvent>());
        lock (_lock)
        {
            events.Add(progress);
        }

        // Raise event — subscribers marshal to their own sync context
        OnProgress?.Invoke(progress);

        // Prune history for terminal states after a short delay
        if (progress.Status is JobStatus.Completed or JobStatus.Failed)
        {
            _ = Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None).ContinueWith(
                t => _history.TryRemove(progress.JobId, out _),
                TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the event history for a given job, or an empty list if none exists.
    /// Used by late-joining Blazor components to catch up on missed events.
    /// </summary>
    public IReadOnlyList<JobProgressEvent> GetHistory(Guid jobId)
    {
        if (_history.TryGetValue(jobId, out var events))
        {
            lock (_lock)
            {
                return events.ToList();
            }
        }
        return [];
    }
}
