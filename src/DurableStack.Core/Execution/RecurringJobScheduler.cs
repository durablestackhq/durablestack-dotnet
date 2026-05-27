using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Core.Scheduling;

namespace DurableStack.Core.Execution;

public sealed class RecurringJobScheduler : IRecurringJobScheduler
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;
    private readonly DurableStackOptions _options;

    public RecurringJobScheduler(IDurableJobStore store, IDurableJobRegistry registry, DurableStackOptions options)
    {
        _store = store;
        _registry = registry;
        _options = options;
    }

    public async Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var due = await _store.GetDueRecurringJobsAsync(nowUtc, 100, cancellationToken);

        var created = 0;

        foreach (var recurring in due)
        {
            var registration = _registry.FindByName(recurring.JobName);
            if (registration is null)
            {
                continue;
            }

            var next = CronScheduleCalculator.GetNextOccurrenceUtc(
                recurring.CronExpression,
                recurring.TimeZone,
                recurring.NextRunAtUtc);

            if (_options.Recurring.CatchUpPolicy == RecurringCatchUpPolicy.SkipMissed)
            {
                while (next <= nowUtc)
                {
                    next = CronScheduleCalculator.GetNextOccurrenceUtc(
                        recurring.CronExpression,
                        recurring.TimeZone,
                        next);
                }
            }

            var materialized = await _store.TryMaterializeRecurringRunAsync(
                recurring,
                registration,
                next,
                cancellationToken);

            if (materialized)
            {
                created += 1;
            }
        }

        return created;
    }
}
