using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Tests;

public sealed class LeaseHeartbeatJobRunnerTests
{
    [Fact]
    public async Task RunAsync_survives_transient_heartbeat_failure_and_keeps_extending()
    {
        var secondExtendAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeExtendLeaseStore(attempt =>
        {
            if (attempt == 1)
            {
                throw new InvalidOperationException("transient lease extension failure");
            }

            secondExtendAttempted.TrySetResult();
            return Task.FromResult(true);
        });
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            LeaseDuration = TimeSpan.FromMilliseconds(300),
        };

        var inner = new DelegatingRunner(async (_, ct) =>
        {
            // Hold the job open until the heartbeat loop has recovered from its first
            // failure and extended the lease a second time.
            await secondExtendAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });

        var runner = new LeaseHeartbeatJobRunner(inner, store, options);
        var run = new JobRunRecord { Id = Guid.NewGuid(), JobName = "heartbeat-test" };

        // With the pre-fix behavior the first ExtendLeaseAsync failure kills the heartbeat
        // loop and rethrows from RunAsync's finally, so this would throw.
        await runner.RunAsync(run, CancellationToken.None);

        Assert.True(store.ExtendAttempts >= 2);
    }

    [Fact]
    public async Task RunAsync_cancels_local_execution_when_lease_is_lost()
    {
        var store = new FakeExtendLeaseStore(_ => Task.FromResult(false));
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            LeaseDuration = TimeSpan.FromMilliseconds(300),
        };

        var inner = new DelegatingRunner(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        var runner = new LeaseHeartbeatJobRunner(inner, store, options);
        var run = new JobRunRecord { Id = Guid.NewGuid(), JobName = "lease-lost-test" };

        // The first heartbeat discovers the lease is gone and must cancel the job
        // instead of letting it run to completion as a duplicate.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(run, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(store.ExtendAttempts >= 1);
    }

    private sealed class DelegatingRunner : IDurableJobRunner
    {
        private readonly Func<JobRunRecord, CancellationToken, Task> _run;

        public DelegatingRunner(Func<JobRunRecord, CancellationToken, Task> run)
        {
            _run = run;
        }

        public Task RunAsync(JobRunRecord run, CancellationToken cancellationToken)
        {
            return _run(run, cancellationToken);
        }
    }

    private sealed class FakeExtendLeaseStore : IDurableJobStore
    {
        private readonly Func<int, Task<bool>> _extendLease;
        private int _extendAttempts;

        public FakeExtendLeaseStore(Func<int, Task<bool>> extendLease)
        {
            _extendLease = extendLease;
        }

        public int ExtendAttempts => Volatile.Read(ref _extendAttempts);

        public Task<bool> ExtendLeaseAsync(Guid runId, string workerName, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _extendAttempts);
            return _extendLease(attempt);
        }

        public Task<Guid> EnqueueAsync(string jobName, string jobType, string? payloadJson, DateTimeOffset scheduledForUtc, int maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(string workerName, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> MarkFailedAsync(Guid runId, string workerName, Exception exception, bool retry, DateTimeOffset? retryAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(string jobName, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(string status, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> TryEnqueueIfNoActiveRunAsync(string jobName, string jobType, string? payloadJson, DateTimeOffset scheduledForUtc, int maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(bool includeDisabled, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> SetRecurringJobEnabledAsync(string jobName, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateRecurringJobScheduleAsync(string jobName, string cronExpression, string timeZone, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneHistoricalRunsAsync(DateTimeOffset completedBeforeUtc, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertRecurringJobAsync(DurableJobRegistration registration, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(DateTimeOffset nowUtc, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateRecurringNextRunAsync(string jobName, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryMaterializeRecurringRunAsync(RecurringJobState recurring, DurableJobRegistration registration, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
