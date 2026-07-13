using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Extensions.Logging;

namespace DurableStack.Core.Execution;

/// <summary>
/// Decorates an <see cref="IDurableJobRunner"/> with a lease heartbeat: while the inner
/// runner executes, a background loop extends the run's lease at half the lease duration
/// (at least every 250 ms). If an extension is refused — the lease was reclaimed by another
/// worker or the run was cancelled — the local execution is cancelled so it cannot race the
/// new owner to completion. Transient heartbeat errors are logged and retried at the next
/// interval.
/// </summary>
public sealed class LeaseHeartbeatJobRunner : IDurableJobRunner
{
    private readonly IDurableJobRunner _inner;
    private readonly IDurableJobStore _store;
    private readonly DurableStackOptions _options;
    private readonly ILogger<LeaseHeartbeatJobRunner>? _logger;

    /// <summary>
    /// Creates a heartbeat wrapper around <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">Runner that performs the actual job execution.</param>
    /// <param name="store">Store used to extend the run's lease.</param>
    /// <param name="options">Configuration supplying the lease duration and worker name.</param>
    /// <param name="logger">Optional logger for heartbeat failures and lease-loss warnings.</param>
    public LeaseHeartbeatJobRunner(
        IDurableJobRunner inner,
        IDurableJobStore store,
        DurableStackOptions options,
        ILogger<LeaseHeartbeatJobRunner>? logger = null)
    {
        _inner = inner;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RunAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        var heartbeatInterval = TimeSpan.FromMilliseconds(Math.Max(250, _options.LeaseDuration.TotalMilliseconds / 2));
        var stopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The job runs on a linked token so that losing the lease (reclaimed by another
        // worker, or the run was cancelled) stops the local execution instead of letting
        // it race the new owner to completion.
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The heartbeat task must never fault or cancel: it is awaited in the finally
        // below, and an exception there would replace the job's own outcome — turning a
        // successful run into a spurious failure/retry after the lease has already lapsed.
        var heartbeatTask = Task.Run(
            async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var completed = await Task.WhenAny(Task.Delay(heartbeatInterval), stopSignal.Task);
                    if (completed == stopSignal.Task)
                    {
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        var extended = await _store.ExtendLeaseAsync(run.Id, _options.WorkerName, _options.LeaseDuration, cancellationToken);
                        if (!extended)
                        {
                            _logger?.LogWarning(
                                "DurableStack lease is no longer held by this worker; cancelling the local execution. RunId={RunId} JobName={JobName}",
                                run.Id,
                                run.JobName);
                            executionCts.Cancel();
                            break;
                        }

                        DurableStackTelemetry.LeaseExtensions.Add(1);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(
                            ex,
                            "DurableStack lease heartbeat failed; retrying at next heartbeat interval. RunId={RunId} JobName={JobName}",
                            run.Id,
                            run.JobName);
                    }
                }
            },
            CancellationToken.None);

        try
        {
            await _inner.RunAsync(run, executionCts.Token);
        }
        finally
        {
            stopSignal.TrySetResult();
            await heartbeatTask;
        }
    }
}
