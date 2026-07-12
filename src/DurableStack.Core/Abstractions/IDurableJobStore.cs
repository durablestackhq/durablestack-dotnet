using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

public interface IDurableJobStore
{
    Task<Guid> EnqueueAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken);

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

    Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<Guid?> TryEnqueueIfNoActiveRunAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(
        bool includeDisabled,
        CancellationToken cancellationToken);

    Task<bool> SetRecurringJobEnabledAsync(
        string jobName,
        bool enabled,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken);

    Task<bool> UpdateRecurringJobScheduleAsync(
        string jobName,
        string cronExpression,
        string timeZone,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken);

    Task UpsertRecurringJobAsync(
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken);

    Task UpdateRecurringNextRunAsync(
        string jobName,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken);

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
