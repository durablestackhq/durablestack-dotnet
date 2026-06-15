using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Core.Scheduling;

namespace DurableStack.Core.Execution;

public sealed class RecurringJobInitializer : IRecurringJobInitializer
{
    private readonly IDurableJobRegistry _registry;
    private readonly IDurableJobStore _store;
    private readonly DurableStackOptions _options;

    public RecurringJobInitializer(
        IDurableJobRegistry registry,
        IDurableJobStore store,
        DurableStackOptions options)
    {
        _registry = registry;
        _store = store;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var recurring = _registry.GetRecurringJobs();
        var knownJobNames = new HashSet<string>(
            _registry.GetAllJobs().Select(x => x.JobName),
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RecurringJobState>? existingSchedules = null;

        if (_options.Recurring.RegistrationSync.ExistingJobBehavior == ExistingRecurringJobBehavior.KeepDatabase
            || _options.Recurring.RegistrationSync.OrphanedJobBehavior == OrphanedRecurringJobBehavior.Disable)
        {
            existingSchedules = await _store.GetRecurringJobsAsync(includeDisabled: true, cancellationToken);
        }

        if (_options.Recurring.RegistrationSync.OrphanedJobBehavior == OrphanedRecurringJobBehavior.Disable)
        {
            foreach (var existingSchedule in existingSchedules ?? Array.Empty<RecurringJobState>())
            {
                if (knownJobNames.Contains(existingSchedule.JobName))
                {
                    continue;
                }

                _ = await _store.SetRecurringJobEnabledAsync(
                    existingSchedule.JobName,
                    enabled: false,
                    nextRunAtUtc: null,
                    cancellationToken);
            }
        }

        foreach (var registration in recurring)
        {
            var now = DateTimeOffset.UtcNow;
            var next = CronScheduleCalculator.GetNextOccurrenceUtc(
                registration.CronExpression!,
                registration.TimeZone,
                now);

            if (_options.Recurring.RegistrationSync.ExistingJobBehavior == ExistingRecurringJobBehavior.KeepDatabase)
            {
                if ((existingSchedules ?? Array.Empty<RecurringJobState>())
                    .Any(x => x.JobName.Equals(registration.JobName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            await _store.UpsertRecurringJobAsync(registration, next, cancellationToken);
        }
    }
}
