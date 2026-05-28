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

    Task MarkSucceededAsync(Guid runId, CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid runId,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken);

    Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken);

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

    Task ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
