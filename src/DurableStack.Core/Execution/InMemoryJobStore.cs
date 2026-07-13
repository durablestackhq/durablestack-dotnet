using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;

namespace DurableStack.Core.Execution;

/// <summary>
/// Non-durable, single-process job store used as the default when no database provider is
/// configured. All state lives in process memory behind a single lock: nothing survives a
/// restart, and multiple workers cannot share it — use a database-backed store for
/// production. Semantics mirror the durable stores, including lease-fenced completion
/// writes, poison-run quarantine on claim, and optimistic recurring-run materialization.
/// </summary>
public sealed class InMemoryJobStore : IDurableJobStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobRunRecord> _runs = new();
    private readonly Dictionary<string, RecurringJobState> _recurring = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<Guid> EnqueueAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var run = new JobRunRecord
        {
            Id = Guid.NewGuid(),
            JobName = jobName,
            JobType = jobType,
            Status = "pending",
            ScheduledForUtc = scheduledForUtc,
            Attempt = 0,
            MaxAttempts = maxAttempts,
            PayloadJson = payloadJson,
        };

        lock (_gate)
        {
            _runs[run.Id] = run;
        }

        return Task.FromResult(run.Id);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = new List<JobRunRecord>();

        lock (_gate)
        {
            // Quarantine poison runs: a run whose lease expired with no attempts left
            // (worker crashed mid-execution) is failed terminally instead of being
            // reclaimed and crash-looping forever.
            var exhausted = _runs.Values
                .Where(run =>
                    run.Status == "leased"
                    && run.LeaseUntilUtc.HasValue
                    && run.LeaseUntilUtc.Value <= now
                    && run.Attempt >= run.MaxAttempts)
                .ToList();
            foreach (var run in exhausted)
            {
                run.Status = "failed";
                run.CompletedAtUtc = now;
                run.LeaseOwner = null;
                run.LeaseUntilUtc = null;
                run.ErrorMessage = "Lease expired with no attempts remaining; the worker likely crashed during execution.";
            }

            var due = _runs.Values
                .Where(run =>
                    run.Attempt < run.MaxAttempts
                    && ((run.Status == "pending" && run.ScheduledForUtc <= now)
                        || (run.Status == "leased" && run.LeaseUntilUtc.HasValue && run.LeaseUntilUtc.Value <= now)))
                .OrderBy(run => run.ScheduledForUtc)
                .Take(batchSize)
                .ToList();

            foreach (var run in due)
            {
                run.Status = "leased";
                run.LeaseOwner = workerName;
                run.LeaseUntilUtc = now.Add(leaseDuration);
                run.StartedAtUtc ??= now;
                run.Attempt += 1;
                claimed.Add(Clone(run));
            }
        }

        return Task.FromResult<IReadOnlyList<JobRunRecord>>(claimed);
    }

    /// <inheritdoc />
    public Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!HoldsLease(runId, workerName, out var run))
            {
                return Task.FromResult(false);
            }

            run.Status = "succeeded";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.LeaseOwner = null;
            run.LeaseUntilUtc = null;
            run.ErrorMessage = null;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
            {
                return Task.FromResult(false);
            }

            if (run.Status == "succeeded" || run.Status == "failed")
            {
                return Task.FromResult(false);
            }

            run.Status = "failed";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.LeaseOwner = null;
            run.LeaseUntilUtc = null;
            run.ErrorMessage = "Run was cancelled.";
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> MarkFailedAsync(
        Guid runId,
        string workerName,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!HoldsLease(runId, workerName, out var run))
            {
                return Task.FromResult(false);
            }

            run.ErrorMessage = exception.Message;
            run.LeaseOwner = null;
            run.LeaseUntilUtc = null;

            if (retry && retryAtUtc.HasValue)
            {
                run.Status = "pending";
                run.ScheduledForUtc = retryAtUtc.Value;
                run.CompletedAtUtc = null;
            }
            else
            {
                run.Status = "failed";
                run.CompletedAtUtc = DateTimeOffset.UtcNow;
            }

            return Task.FromResult(true);
        }
    }

    private bool HoldsLease(Guid runId, string workerName, out JobRunRecord run)
    {
        // Completion writes are fenced to the lease owner so a worker whose lease was
        // reclaimed (or whose run was cancelled) cannot overwrite the new owner's state.
        if (_runs.TryGetValue(runId, out var found)
            && found.Status == "leased"
            && string.Equals(found.LeaseOwner, workerName, StringComparison.Ordinal))
        {
            run = found;
            return true;
        }

        run = null!;
        return false;
    }

    /// <inheritdoc />
    public Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var run))
            {
                return Task.FromResult<JobRunRecord?>(Clone(run));
            }
        }

        return Task.FromResult<JobRunRecord?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values.Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values
                .OrderByDescending(x => x.ScheduledForUtc)
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values
                .Where(x => x.JobName.Equals(jobName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ScheduledForUtc)
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values
                .Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ScheduledForUtc)
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values
                .Where(x => x.ScheduleSlotUtc is null)
                .OrderByDescending(x => x.ScheduledForUtc)
                .Take(Math.Max(1, take))
                .Select(Clone)
                .ToList();

            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

    /// <inheritdoc />
    public Task<Guid?> TryEnqueueIfNoActiveRunAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var hasActive = _runs.Values.Any(
                x => x.JobName.Equals(jobName, StringComparison.OrdinalIgnoreCase)
                    && (x.Status == "pending" || x.Status == "leased"));

            if (hasActive)
            {
                return Task.FromResult<Guid?>(null);
            }

            var run = new JobRunRecord
            {
                Id = Guid.NewGuid(),
                JobName = jobName,
                JobType = jobType,
                Status = "pending",
                ScheduledForUtc = scheduledForUtc,
                Attempt = 0,
                MaxAttempts = maxAttempts,
                PayloadJson = payloadJson,
            };

            _runs[run.Id] = run;
            return Task.FromResult<Guid?>(run.Id);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var jobs = _recurring.Values
                .Where(x => includeDisabled || x.Enabled)
                .OrderBy(x => x.JobName)
                .Select(CloneRecurring)
                .ToList();

            return Task.FromResult<IReadOnlyList<RecurringJobState>>(jobs);
        }
    }

    /// <inheritdoc />
    public Task<bool> SetRecurringJobEnabledAsync(
        string jobName,
        bool enabled,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_recurring.TryGetValue(jobName, out var state))
            {
                return Task.FromResult(false);
            }

            var next = nextRunAtUtc ?? state.NextRunAtUtc;
            _recurring[jobName] = new RecurringJobState
            {
                JobName = state.JobName,
                JobType = state.JobType,
                CronExpression = state.CronExpression,
                TimeZone = state.TimeZone,
                MaxAttempts = state.MaxAttempts,
                Enabled = enabled,
                AllowConcurrentRuns = state.AllowConcurrentRuns,
                RetryBehavior = state.RetryBehavior,
                RetryInitialDelaySeconds = state.RetryInitialDelaySeconds,
                NextRunAtUtc = next,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> UpdateRecurringJobScheduleAsync(
        string jobName,
        string cronExpression,
        string timeZone,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_recurring.TryGetValue(jobName, out var state))
            {
                return Task.FromResult(false);
            }

            _recurring[jobName] = new RecurringJobState
            {
                JobName = state.JobName,
                JobType = state.JobType,
                CronExpression = cronExpression,
                TimeZone = timeZone,
                MaxAttempts = state.MaxAttempts,
                Enabled = state.Enabled,
                AllowConcurrentRuns = state.AllowConcurrentRuns,
                RetryBehavior = state.RetryBehavior,
                RetryInitialDelaySeconds = state.RetryInitialDelaySeconds,
                NextRunAtUtc = nextRunAtUtc,
            };

            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var toDelete = _runs.Values
                .Where(run =>
                    (run.Status == "succeeded" || run.Status == "failed")
                    && run.CompletedAtUtc.HasValue
                    && run.CompletedAtUtc.Value < completedBeforeUtc)
                .OrderBy(run => run.CompletedAtUtc)
                .Take(Math.Max(1, batchSize))
                .Select(run => run.Id)
                .ToList();

            foreach (var runId in toDelete)
            {
                _runs.Remove(runId);
            }

            return Task.FromResult(toDelete.Count);
        }
    }

    /// <inheritdoc />
    public Task UpsertRecurringJobAsync(
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        if (!registration.IsRecurring || string.IsNullOrWhiteSpace(registration.CronExpression))
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            _recurring[registration.JobName] = new RecurringJobState
            {
                JobName = registration.JobName,
                JobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
                CronExpression = registration.CronExpression,
                TimeZone = registration.TimeZone,
                MaxAttempts = registration.MaxAttempts,
                Enabled = registration.Enabled,
                AllowConcurrentRuns = registration.AllowConcurrentRuns,
                RetryBehavior = registration.RetryBehavior,
                RetryInitialDelaySeconds = registration.RetryInitialDelaySeconds,
                NextRunAtUtc = nextRunAtUtc,
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var due = _recurring.Values
                .Where(x => x.NextRunAtUtc <= nowUtc)
                .Where(x => x.Enabled)
                .OrderBy(x => x.NextRunAtUtc)
                .Take(batchSize)
                .Select(CloneRecurring)
                .ToList();

            return Task.FromResult<IReadOnlyList<RecurringJobState>>(due);
        }
    }

    /// <inheritdoc />
    public Task UpdateRecurringNextRunAsync(
        string jobName,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_recurring.TryGetValue(jobName, out var state))
            {
                state.NextRunAtUtc = nextRunAtUtc;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_recurring.TryGetValue(recurring.JobName, out var state))
            {
                return Task.FromResult(false);
            }

            if (state.NextRunAtUtc != recurring.NextRunAtUtc)
            {
                return Task.FromResult(false);
            }

            var hasPending = _runs.Values.Any(x => x.JobName.Equals(state.JobName, StringComparison.OrdinalIgnoreCase)
                && x.Status == "pending");
            var hasLeased = _runs.Values.Any(x => x.JobName.Equals(state.JobName, StringComparison.OrdinalIgnoreCase)
                && x.Status == "leased");

            if ((!state.AllowConcurrentRuns && (hasPending || hasLeased))
                || (state.AllowConcurrentRuns && hasPending))
            {
                return Task.FromResult(false);
            }

            var run = new JobRunRecord
            {
                Id = Guid.NewGuid(),
                JobName = registration.JobName,
                JobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
                Status = "pending",
                ScheduledForUtc = recurring.NextRunAtUtc,
                ScheduleSlotUtc = recurring.NextRunAtUtc,
                Attempt = 0,
                MaxAttempts = registration.MaxAttempts,
                PayloadJson = null,
            };

            _runs[run.Id] = run;
            state.NextRunAtUtc = nextRunAtUtc;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var run)
                && run.Status == "leased"
                && string.Equals(run.LeaseOwner, workerName, StringComparison.Ordinal))
            {
                run.LeaseUntilUtc = DateTimeOffset.UtcNow.Add(leaseDuration);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    private static JobRunRecord Clone(JobRunRecord source)
    {
        return new JobRunRecord
        {
            Id = source.Id,
            JobName = source.JobName,
            JobType = source.JobType,
            Status = source.Status,
            ScheduledForUtc = source.ScheduledForUtc,
            ScheduleSlotUtc = source.ScheduleSlotUtc,
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            Attempt = source.Attempt,
            MaxAttempts = source.MaxAttempts,
            LeaseOwner = source.LeaseOwner,
            LeaseUntilUtc = source.LeaseUntilUtc,
            PayloadJson = source.PayloadJson,
            ErrorMessage = source.ErrorMessage,
        };
    }

    private static RecurringJobState CloneRecurring(RecurringJobState source)
    {
        return new RecurringJobState
        {
            JobName = source.JobName,
            JobType = source.JobType,
            CronExpression = source.CronExpression,
            TimeZone = source.TimeZone,
            MaxAttempts = source.MaxAttempts,
            Enabled = source.Enabled,
            AllowConcurrentRuns = source.AllowConcurrentRuns,
            RetryBehavior = source.RetryBehavior,
            RetryInitialDelaySeconds = source.RetryInitialDelaySeconds,
            NextRunAtUtc = source.NextRunAtUtc,
        };
    }
}
