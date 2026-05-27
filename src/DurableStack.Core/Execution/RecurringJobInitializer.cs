using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Scheduling;

namespace DurableStack.Core.Execution;

public sealed class RecurringJobInitializer : IRecurringJobInitializer
{
    private readonly IDurableJobRegistry _registry;
    private readonly IDurableJobStore _store;

    public RecurringJobInitializer(IDurableJobRegistry registry, IDurableJobStore store)
    {
        _registry = registry;
        _store = store;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var recurring = _registry.GetRecurringJobs();

        foreach (var registration in recurring)
        {
            var now = DateTimeOffset.UtcNow;
            var next = CronScheduleCalculator.GetNextOccurrenceUtc(
                registration.CronExpression!,
                registration.TimeZone,
                now);

            await _store.UpsertRecurringJobAsync(registration, next, cancellationToken);
        }
    }
}
