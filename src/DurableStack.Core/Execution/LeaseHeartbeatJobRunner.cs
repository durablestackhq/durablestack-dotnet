using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Core.Execution;

public sealed class LeaseHeartbeatJobRunner : IDurableJobRunner
{
    private readonly IDurableJobRunner _inner;
    private readonly IDurableJobStore _store;
    private readonly DurableStackOptions _options;

    public LeaseHeartbeatJobRunner(IDurableJobRunner inner, IDurableJobStore store, DurableStackOptions options)
    {
        _inner = inner;
        _store = store;
        _options = options;
    }

    public async Task RunAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        var heartbeatInterval = TimeSpan.FromMilliseconds(Math.Max(250, _options.LeaseDuration.TotalMilliseconds / 2));
        var stopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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

                    await _store.ExtendLeaseAsync(run.Id, _options.WorkerName, _options.LeaseDuration, cancellationToken);
                    DurableStackTelemetry.LeaseExtensions.Add(1);
                }
            },
            cancellationToken);

        try
        {
            await _inner.RunAsync(run, cancellationToken);
        }
        finally
        {
            stopSignal.TrySetResult();
            await heartbeatTask;
        }
    }
}
