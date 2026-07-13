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

/// <summary>
/// Synchronizes code-registered recurring jobs into the store at startup. Each recurring
/// registration is upserted with its computed next occurrence — unless
/// <c>ExistingJobBehavior</c> is <see cref="ExistingRecurringJobBehavior.KeepDatabase"/>, in
/// which case schedules already present in the store are left untouched. When
/// <c>OrphanedJobBehavior</c> is <see cref="OrphanedRecurringJobBehavior.Disable"/>, stored
/// schedules with no matching registration are disabled.
/// </summary>
public sealed class RecurringJobInitializer : IRecurringJobInitializer
{
    private readonly IDurableJobRegistry _registry;
    private readonly IDurableJobStore _store;
    private readonly DurableStackOptions _options;

    /// <summary>
    /// Creates an initializer that syncs registrations from <paramref name="registry"/>
    /// into <paramref name="store"/> per the recurring registration-sync options.
    /// </summary>
    /// <param name="registry">Source of the code-registered recurring jobs.</param>
    /// <param name="store">Store whose recurring schedules are created or updated.</param>
    /// <param name="options">Configuration supplying the registration-sync behaviors.</param>
    public RecurringJobInitializer(
        IDurableJobRegistry registry,
        IDurableJobStore store,
        DurableStackOptions options)
    {
        _registry = registry;
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
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
