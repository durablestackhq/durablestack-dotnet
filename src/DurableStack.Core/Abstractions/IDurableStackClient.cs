using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Application-facing entry point for putting work on the queue: enqueue a job now,
/// schedule it for later, or cancel a run. The run is persisted durably and picked up by
/// whichever worker claims it first.
/// </summary>
public interface IDurableStackClient
{
    /// <summary>
    /// Persists a run of <typeparamref name="TJob"/> eligible to execute immediately and
    /// returns its run id. Throws <see cref="InvalidOperationException"/> when
    /// <typeparamref name="TJob"/> has not been registered.
    /// </summary>
    /// <typeparam name="TJob">The registered job type to run.</typeparam>
    /// <param name="payload">Arguments serialized to JSON and passed to the job, or null for jobs without arguments.</param>
    /// <param name="cancellationToken">Token that cancels the enqueue (not the eventual execution).</param>
    Task<Guid> EnqueueAsync<TJob>(object? payload = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a run of <typeparamref name="TJob"/> that becomes eligible to execute at
    /// <paramref name="runAtUtc"/> and returns its run id. Execution starts at the first
    /// worker poll after that time, not at the exact instant. Throws
    /// <see cref="InvalidOperationException"/> when <typeparamref name="TJob"/> has not been
    /// registered.
    /// </summary>
    /// <typeparam name="TJob">The registered job type to run.</typeparam>
    /// <param name="payload">Arguments serialized to JSON and passed to the job, or null for jobs without arguments.</param>
    /// <param name="runAtUtc">Earliest time the run may execute.</param>
    /// <param name="cancellationToken">Token that cancels the enqueue (not the eventual execution).</param>
    Task<Guid> ScheduleAsync<TJob>(
        object? payload,
        DateTimeOffset runAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a run that has not yet completed. Returns false when the run does not exist
    /// or has already succeeded or failed. A run already executing is not interrupted, but
    /// its outcome is discarded because the completing worker no longer holds the lease.
    /// </summary>
    /// <param name="runId">Id of the run to cancel, as returned by the enqueue methods.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default);
}
