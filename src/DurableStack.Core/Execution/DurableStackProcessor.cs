using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Core.Execution;

public sealed class DurableStackProcessor : IDurableStackProcessor
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRunner _runner;
    private readonly IRecurringJobScheduler _recurringScheduler;
    private readonly DurableStackOptions _options;
    private readonly IReadOnlyList<IDurableStackEventSink> _eventSinks;
    private readonly DurableStackEventFactory _eventFactory;

    public DurableStackProcessor(
        IDurableJobStore store,
        IDurableJobRunner runner,
        IRecurringJobScheduler recurringScheduler,
        DurableStackOptions options,
        IEnumerable<IDurableStackEventSink> eventSinks,
        DurableStackEventFactory eventFactory)
    {
        _store = store;
        _runner = runner;
        _recurringScheduler = recurringScheduler;
        _options = options;
        _eventSinks = eventSinks.ToList();
        _eventFactory = eventFactory;
    }

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        DurableStackTelemetry.WorkerPolls.Add(1);

        var materialized = await _recurringScheduler.MaterializeDueRunsAsync(cancellationToken);
        if (materialized > 0)
        {
            DurableStackTelemetry.RecurringRunsMaterialized.Add(materialized);
        }

        var claimed = await _store.ClaimDueRunsAsync(
            _options.WorkerName,
            _options.BatchSize,
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

            await ExecuteRunAsync(run, cancellationToken);
        }

        return claimed.Count;
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
            DateTimeOffset? retryAt = shouldRetry ? DateTimeOffset.UtcNow.Add(_options.RetryDelay) : null;
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

    private async Task PublishEventAsync(DurableStackEvent @event, CancellationToken cancellationToken)
    {
        foreach (var sink in _eventSinks)
        {
            await sink.PublishAsync(@event, cancellationToken);
        }
    }
}
