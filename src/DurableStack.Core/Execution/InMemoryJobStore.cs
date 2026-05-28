using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;

namespace DurableStack.Core.Execution;

public sealed class InMemoryJobStore : IDurableJobStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobRunRecord> _runs = new();
    private readonly Dictionary<string, RecurringJobState> _recurring = new(StringComparer.OrdinalIgnoreCase);

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
            var due = _runs.Values
                .Where(run =>
                    (run.Status == "pending" && run.ScheduledForUtc <= now)
                    || (run.Status == "leased" && run.LeaseUntilUtc.HasValue && run.LeaseUntilUtc.Value <= now))
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

    public Task MarkSucceededAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var run))
            {
                run.Status = "succeeded";
                run.CompletedAtUtc = DateTimeOffset.UtcNow;
                run.LeaseOwner = null;
                run.LeaseUntilUtc = null;
                run.ErrorMessage = null;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        Guid runId,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var run))
            {
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
            }
        }

        return Task.CompletedTask;
    }

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

    public Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = _runs.Values.Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<JobRunRecord>>(result);
        }
    }

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
                CronExpression = registration.CronExpression,
                TimeZone = registration.TimeZone,
                NextRunAtUtc = nextRunAtUtc,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var due = _recurring.Values
                .Where(x => x.NextRunAtUtc <= nowUtc)
                .OrderBy(x => x.NextRunAtUtc)
                .Take(batchSize)
                .Select(x => new RecurringJobState
                {
                    JobName = x.JobName,
                    CronExpression = x.CronExpression,
                    TimeZone = x.TimeZone,
                    NextRunAtUtc = x.NextRunAtUtc,
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<RecurringJobState>>(due);
        }
    }

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

            var run = new JobRunRecord
            {
                Id = Guid.NewGuid(),
                JobName = registration.JobName,
                JobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
                Status = "pending",
                ScheduledForUtc = recurring.NextRunAtUtc,
                Attempt = 0,
                MaxAttempts = registration.MaxAttempts,
                PayloadJson = null,
            };

            _runs[run.Id] = run;
            state.NextRunAtUtc = nextRunAtUtc;
            return Task.FromResult(true);
        }
    }

    public Task ExtendLeaseAsync(
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
            }
        }

        return Task.CompletedTask;
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
}
