using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Persistence contract for the job system. The store is the single source of truth that
/// gives DurableStack its at-least-once guarantee: runs are claimed with a time-bounded
/// lease, completion writes are fenced to the lease owner, and expired leases make runs
/// visible to other workers again. Implementations exist per storage provider (Postgres,
/// SQL Server, SQLite, MySQL, in-memory).
/// </summary>
public interface IDurableJobStore
{
    /// <summary>
    /// Persists a new run in the pending state and returns its id. The run becomes eligible
    /// for claiming once <paramref name="scheduledForUtc"/> has passed.
    /// </summary>
    /// <param name="jobName">Registered job name used to resolve the executing type at run time.</param>
    /// <param name="jobType">Assembly-qualified name of the job's CLR type, stored for diagnostics.</param>
    /// <param name="payloadJson">JSON-serialized payload, or null for jobs without arguments.</param>
    /// <param name="scheduledForUtc">Earliest time the run may execute; use the current time to run as soon as possible.</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before the run is terminally failed.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<Guid> EnqueueAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> due runs for
    /// <paramref name="workerName"/>: pending runs whose scheduled time has passed, plus
    /// leased runs whose lease has expired (their worker is presumed dead). Claiming
    /// increments the attempt counter and sets a lease that expires after
    /// <paramref name="leaseDuration"/> unless extended. Expired runs with no attempts
    /// remaining are terminally failed (poison quarantine) rather than reclaimed.
    /// </summary>
    /// <param name="workerName">Identity recorded as the lease owner; used to fence later completion writes.</param>
    /// <param name="batchSize">Maximum number of runs to claim in this call.</param>
    /// <param name="leaseDuration">How long the claim remains exclusive without a heartbeat.</param>
    /// <param name="cancellationToken">Token that cancels the claim.</param>
    Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful run outcome. The write is fenced: it only applies while
    /// <paramref name="workerName"/> still holds the run's lease. Returns false when the
    /// lease was lost (expired and reclaimed, or the run was cancelled), in which case
    /// the run state is left untouched.
    /// </summary>
    Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a run that has not yet completed, recording it as failed with a cancellation
    /// message and releasing any lease. Returns false when the run does not exist or has
    /// already succeeded or failed. A worker mid-execution is not interrupted, but its
    /// completion write will be fenced out.
    /// </summary>
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a failed run outcome. Fenced the same way as
    /// <see cref="MarkSucceededAsync(Guid, string, CancellationToken)"/>: returns false and
    /// leaves the run untouched when <paramref name="workerName"/> no longer holds the lease.
    /// </summary>
    Task<bool> MarkFailedAsync(
        Guid runId,
        string workerName,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a snapshot of a single run by id; returns null when no run with that id exists
    /// (including runs already deleted by retention).
    /// </summary>
    Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recently scheduled runs across all jobs and statuses, capped at
    /// <paramref name="take"/>.
    /// </summary>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every stored run without a cap. Intended for tests and small deployments;
    /// prefer the take-limited queries in production.
    /// </summary>
    Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns runs belonging to the given registered job name, capped at
    /// <paramref name="take"/>.
    /// </summary>
    /// <param name="jobName">Registered job name to filter on.</param>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns runs currently in the given status ("pending", "leased", "succeeded", or
    /// "failed"), capped at <paramref name="take"/>.
    /// </summary>
    /// <param name="status">Status value to filter on.</param>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns runs waiting to execute (pending and not yet claimed), capped at
    /// <paramref name="take"/>.
    /// </summary>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a run only when the job has no active (pending or leased) run, enforcing
    /// single-flight execution per job name. Returns the new run's id, or null when an
    /// active run blocked the enqueue. Used to trigger non-concurrent recurring jobs on
    /// demand.
    /// </summary>
    /// <param name="jobName">Registered job name used to resolve the executing type at run time.</param>
    /// <param name="jobType">Assembly-qualified name of the job's CLR type, stored for diagnostics.</param>
    /// <param name="payloadJson">JSON-serialized payload, or null for jobs without arguments.</param>
    /// <param name="scheduledForUtc">Earliest time the run may execute.</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before the run is terminally failed.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<Guid?> TryEnqueueIfNoActiveRunAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the stored recurring schedule rows, ordered by job name.
    /// </summary>
    /// <param name="includeDisabled">When false, only schedules currently enabled are returned.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(
        bool includeDisabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enables or disables a recurring schedule. Returns false when no schedule with that
    /// name exists. Disabled schedules are not materialized into runs.
    /// </summary>
    /// <param name="jobName">Name of the schedule to update.</param>
    /// <param name="enabled">True to resume materialization, false to pause it.</param>
    /// <param name="nextRunAtUtc">New next-run time to set (typically recomputed when re-enabling); null keeps the stored value.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> SetRecurringJobEnabledAsync(
        string jobName,
        bool enabled,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a recurring schedule's cron expression and time zone and sets its next due
    /// time. Returns false when no schedule with that name exists.
    /// </summary>
    /// <param name="jobName">Name of the schedule to update.</param>
    /// <param name="cronExpression">New five-field cron expression.</param>
    /// <param name="timeZone">IANA time zone id the expression is evaluated in.</param>
    /// <param name="nextRunAtUtc">First occurrence of the new schedule, precomputed by the caller.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> UpdateRecurringJobScheduleAsync(
        string jobName,
        string cronExpression,
        string timeZone,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes completed (succeeded or failed) runs that finished before
    /// <paramref name="completedBeforeUtc"/>, oldest first, up to <paramref name="batchSize"/>
    /// per call. Returns the number of runs deleted. Called periodically by the retention
    /// sweep; pending and leased runs are never touched.
    /// </summary>
    /// <param name="completedBeforeUtc">Cutoff: only runs completed before this time are deleted.</param>
    /// <param name="batchSize">Maximum number of runs to delete in this call, bounding transaction size.</param>
    /// <param name="cancellationToken">Token that cancels the delete.</param>
    Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces the schedule row for a recurring registration, copying its cron,
    /// time zone, retry, and concurrency settings. Non-recurring registrations are ignored.
    /// Called at startup to sync code-declared schedules into the store.
    /// </summary>
    /// <param name="registration">The recurring registration to persist.</param>
    /// <param name="nextRunAtUtc">The schedule's next due time, precomputed from its cron expression.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task UpsertRecurringJobAsync(
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns enabled recurring schedules whose next-run time is at or before
    /// <paramref name="nowUtc"/>, soonest first, capped at <paramref name="batchSize"/>.
    /// </summary>
    /// <param name="nowUtc">The current time used as the due cutoff.</param>
    /// <param name="batchSize">Maximum number of schedules to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets a recurring schedule's next due time without creating a run. Does nothing when
    /// no schedule with that name exists.
    /// </summary>
    /// <param name="jobName">Name of the schedule to update.</param>
    /// <param name="nextRunAtUtc">The new next due time.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task UpdateRecurringNextRunAsync(
        string jobName,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates a run for a due recurring slot and advances the schedule to
    /// <paramref name="nextRunAtUtc"/>. The write is optimistically concurrent on the
    /// schedule's stored next-run time still matching <paramref name="recurring"/>, so each
    /// slot is materialized exactly once across competing workers. Returns false when
    /// another worker won the slot, or when an existing run blocks it (any active run for
    /// non-concurrent schedules; a pending run even for concurrent ones).
    /// </summary>
    /// <param name="recurring">Snapshot of the schedule being materialized; its next-run time acts as the concurrency token.</param>
    /// <param name="registration">The in-code registration supplying job type, attempts, and concurrency settings for the new run.</param>
    /// <param name="nextRunAtUtc">The slot after this one, written as the schedule's new next-run time on success.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extends the lease on a run this worker owns. Returns false when the lease is no
    /// longer held by <paramref name="workerName"/> — the caller should stop executing
    /// the run, because another worker may have reclaimed it.
    /// </summary>
    Task<bool> ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
