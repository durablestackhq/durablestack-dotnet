using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Extensions.Logging;

namespace DurableStack.Core.Execution;

public sealed class DurableStackProcessor : IDurableStackProcessor, IInFlightRunDrainer
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;
    private readonly IDurableJobRunner _runner;
    private readonly IRecurringJobScheduler _recurringScheduler;
    private readonly DurableStackOptions _options;
    private readonly IReadOnlyList<IDurableStackEventSink> _eventSinks;
    private readonly DurableStackEventFactory _eventFactory;
    private readonly ILogger<DurableStackProcessor>? _logger;
    private readonly object _retentionGate = new();
    private readonly object _inFlightGate = new();
    private readonly HashSet<Task> _inFlightRuns = new();
    private DateTimeOffset _nextRetentionSweepAtUtc = DateTimeOffset.MinValue;

    public DurableStackProcessor(
        IDurableJobStore store,
        IDurableJobRegistry registry,
        IDurableJobRunner runner,
        IRecurringJobScheduler recurringScheduler,
        DurableStackOptions options,
        IEnumerable<IDurableStackEventSink> eventSinks,
        DurableStackEventFactory eventFactory,
        ILogger<DurableStackProcessor>? logger = null)
    {
        _store = store;
        _registry = registry;
        _runner = runner;
        _recurringScheduler = recurringScheduler;
        _options = options;
        _eventSinks = eventSinks.ToList();
        _eventFactory = eventFactory;
        _logger = logger;
    }

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        DurableStackTelemetry.WorkerPolls.Add(1);

        await PruneHistoricalRunsIfDueAsync(cancellationToken);

        var materialized = await _recurringScheduler.MaterializeDueRunsAsync(cancellationToken);
        if (materialized > 0)
        {
            DurableStackTelemetry.RecurringRunsMaterialized.Add(materialized);
        }

        var availableExecutionSlots = GetAvailableExecutionSlots();
        if (availableExecutionSlots <= 0)
        {
            return 0;
        }

        var claimCount = Math.Max(1, Math.Min(_options.ClaimBatchSize, availableExecutionSlots));

        var claimed = await _store.ClaimDueRunsAsync(
            _options.WorkerName,
            claimCount,
            _options.LeaseDuration,
            cancellationToken);

        if (claimed.Count > 0)
        {
            DurableStackTelemetry.JobsClaimed.Add(claimed.Count);
        }

        foreach (var run in claimed)
        {
            await PublishEventAsync(
                _eventFactory.Create(DurableStackEventTypes.JobClaimed, run),
                cancellationToken);

            TrackInFlightRun(ExecuteRunSafelyAsync(run, cancellationToken));
        }

        return claimed.Count;
    }

    private int GetAvailableExecutionSlots()
    {
        lock (_inFlightGate)
        {
            _ = _inFlightRuns.RemoveWhere(task => task.IsCompleted);
            return Math.Max(0, _options.MaxConcurrentRuns - _inFlightRuns.Count);
        }
    }

    private void TrackInFlightRun(Task task)
    {
        lock (_inFlightGate)
        {
            _inFlightRuns.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                lock (_inFlightGate)
                {
                    _inFlightRuns.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task DrainInFlightRunsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task[] snapshot;
            lock (_inFlightGate)
            {
                _ = _inFlightRuns.RemoveWhere(task => task.IsCompleted);
                if (_inFlightRuns.Count == 0)
                {
                    return;
                }

                snapshot = _inFlightRuns.ToArray();
            }

            await Task.WhenAll(snapshot).WaitAsync(cancellationToken);
        }
    }

    private async Task ExecuteRunSafelyAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteRunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DurableStack run execution wrapper failure. RunId={RunId} JobName={JobName}", run.Id, run.JobName);
        }
    }

    private async Task PruneHistoricalRunsIfDueAsync(CancellationToken cancellationToken)
    {
        if (!_options.Retention.Enabled)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var shouldSweep = false;
        var sweepInterval = _options.Retention.GetEffectiveSweepInterval();

        lock (_retentionGate)
        {
            if (nowUtc >= _nextRetentionSweepAtUtc)
            {
                _nextRetentionSweepAtUtc = nowUtc.Add(sweepInterval);
                shouldSweep = true;
            }
        }

        if (!shouldSweep)
        {
            return;
        }

        var retentionWindow = _options.Retention.GetEffectiveRunRetention(_options.StorageProvider);
        var completedBeforeUtc = nowUtc.Subtract(retentionWindow);
        var batchSize = _options.Retention.GetEffectiveDeleteBatchSize();

        await _store.PruneHistoricalRunsAsync(completedBeforeUtc, batchSize, cancellationToken);
    }

    private async Task ExecuteRunAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        using var activity = DurableStackTelemetry.ActivitySource.StartActivity("durablestack.job.execute");
        activity?.SetTag("durablestack.run_id", run.Id);
        activity?.SetTag("durablestack.job_name", run.JobName);
        activity?.SetTag("durablestack.attempt", run.Attempt);
        activity?.SetTag("durablestack.worker_name", _options.WorkerName);

        try
        {
            var startedAt = DateTimeOffset.UtcNow;

            await PublishEventAsync(
                _eventFactory.Create(DurableStackEventTypes.JobStarted, run),
                cancellationToken);
            DurableStackTelemetry.JobsStarted.Add(1);

            await _runner.RunAsync(run, cancellationToken);
            await _store.MarkSucceededAsync(run.Id, cancellationToken);
            DurableStackTelemetry.JobsSucceeded.Add(1);

            var duration = DateTimeOffset.UtcNow - startedAt;

            await PublishEventAsync(
                _eventFactory.Create(
                    DurableStackEventTypes.JobSucceeded,
                    run,
                    durationMs: duration.TotalMilliseconds),
                cancellationToken);
        }
        catch (Exception ex)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var shouldRetry = run.Attempt < run.MaxAttempts;
            DateTimeOffset? retryAt = shouldRetry ? DateTimeOffset.UtcNow.Add(CalculateRetryDelay(run)) : null;
            await _store.MarkFailedAsync(run.Id, ex, shouldRetry, retryAt, cancellationToken);
            DurableStackTelemetry.JobsFailed.Add(1);

            await PublishEventAsync(
                _eventFactory.Create(
                    DurableStackEventTypes.JobFailed,
                    run,
                    ex.Message,
                    errorType: ex.GetType().FullName,
                    errorDetail: ex.ToString(),
                    retryAtUtc: retryAt,
                    durationMs: (failedAt - (run.StartedAtUtc ?? failedAt)).TotalMilliseconds),
                cancellationToken);

            if (shouldRetry)
            {
                DurableStackTelemetry.JobsRetried.Add(1);
                await PublishEventAsync(
                    _eventFactory.Create(
                        DurableStackEventTypes.JobRetried,
                        run,
                        retryAtUtc: retryAt,
                        errorType: ex.GetType().FullName),
                    cancellationToken);

                await PublishEventAsync(
                    _eventFactory.Create(
                        DurableStackEventTypes.RetryScheduled,
                        run,
                        retryAt?.ToString("O"),
                        retryAtUtc: retryAt,
                        errorType: ex.GetType().FullName),
                    cancellationToken);
            }
        }
    }

    private TimeSpan CalculateRetryDelay(JobRunRecord run)
    {
        var registration = _registry.FindByName(run.JobName);
        var behavior = registration?.RetryBehavior ?? RetryBehavior.FixedDelay;
        var baseDelay = registration?.RetryInitialDelaySeconds is > 0
            ? TimeSpan.FromSeconds(registration.RetryInitialDelaySeconds.Value)
            : _options.RetryDelay;

        var delay = behavior == RetryBehavior.Backoff
            ? TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, run.Attempt - 1)))
            : baseDelay;

        if (_options.RetryJitterEnabled)
        {
            var ratio = Math.Clamp(_options.RetryJitterRatio, 0, 1);
            var factor = 1 + ((Random.Shared.NextDouble() * 2 - 1) * ratio);
            delay = TimeSpan.FromMilliseconds(Math.Max(0, delay.TotalMilliseconds * factor));
        }

        if (_options.RetryMaxDelay > TimeSpan.Zero && delay > _options.RetryMaxDelay)
        {
            delay = _options.RetryMaxDelay;
        }

        return delay;
    }

    private async Task PublishEventAsync(DurableStackEvent @event, CancellationToken cancellationToken)
    {
        foreach (var sink in _eventSinks)
        {
            await sink.PublishAsync(@event, cancellationToken);
        }
    }
}
