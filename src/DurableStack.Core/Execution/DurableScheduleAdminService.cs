using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Scheduling;

namespace DurableStack.Core.Execution;

public sealed class DurableScheduleAdminService : IDurableScheduleAdminService
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;

    public DurableScheduleAdminService(IDurableJobStore store, IDurableJobRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public Task<IReadOnlyList<RecurringJobState>> ListScheduledJobsAsync(bool includeDisabled = true, CancellationToken cancellationToken = default)
    {
        return _store.GetRecurringJobsAsync(includeDisabled, cancellationToken);
    }

    public async Task<bool> SetScheduledJobEnabledAsync(string jobName, bool enabled, CancellationToken cancellationToken = default)
    {
        var nextRunAtUtc = enabled ? await ResolveNextRunUtcAsync(jobName, cancellationToken) : null;
        return await _store.SetRecurringJobEnabledAsync(jobName, enabled, nextRunAtUtc, cancellationToken);
    }

    public async Task<bool> UpdateScheduledJobCronAsync(
        string jobName,
        string cronExpression,
        string timeZone = "UTC",
        CancellationToken cancellationToken = default)
    {
        var next = CronScheduleCalculator.GetNextOccurrenceUtc(
            cronExpression,
            timeZone,
            DateTimeOffset.UtcNow);

        return await _store.UpdateRecurringJobScheduleAsync(
            jobName,
            cronExpression,
            timeZone,
            next,
            cancellationToken);
    }

    public async Task<Guid?> RunScheduledJobNowAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var registration = _registry.FindByName(jobName);
        if (registration is null || !registration.IsRecurring)
        {
            return null;
        }

        var runId = await _store.EnqueueAsync(
            registration.JobName,
            registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
            payloadJson: null,
            DateTimeOffset.UtcNow,
            registration.MaxAttempts,
            cancellationToken);

        return runId;
    }

    private async Task<DateTimeOffset?> ResolveNextRunUtcAsync(string jobName, CancellationToken cancellationToken)
    {
        var jobs = await _store.GetRecurringJobsAsync(includeDisabled: true, cancellationToken);
        foreach (var job in jobs)
        {
            if (!job.JobName.Equals(jobName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return CronScheduleCalculator.GetNextOccurrenceUtc(
                job.CronExpression,
                job.TimeZone,
                DateTimeOffset.UtcNow);
        }

        return null;
    }
}
