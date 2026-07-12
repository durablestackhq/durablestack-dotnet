using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace DurableStack.Core.Execution;

public sealed class RecurringJobScheduler : IRecurringJobScheduler
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;
    private readonly DurableStackOptions _options;
    private readonly ILogger<RecurringJobScheduler>? _logger;

    public RecurringJobScheduler(
        IDurableJobStore store,
        IDurableJobRegistry registry,
        DurableStackOptions options,
        ILogger<RecurringJobScheduler>? logger = null)
    {
        _store = store;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    public async Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var due = await _store.GetDueRecurringJobsAsync(nowUtc, 100, cancellationToken);

        var created = 0;

        foreach (var recurring in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each schedule is isolated: one row with an unresolvable time zone or a
            // cron expression with no next occurrence must not abort materialization
            // for every other schedule (and, since materialization runs before
            // claiming, all job processing on this worker).
            try
            {
                created += await MaterializeScheduleAsync(recurring, nowUtc, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "DurableStack failed to materialize recurring job; other schedules continue. JobName={JobName} Cron={CronExpression} TimeZone={TimeZone}",
                    recurring.JobName,
                    recurring.CronExpression,
                    recurring.TimeZone);
            }
        }

        return created;
    }

    private async Task<int> MaterializeScheduleAsync(
        Models.RecurringJobState recurring,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var registration = _registry.FindByName(recurring.JobName);
        if (registration is null)
        {
            return 0;
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

        return materialized ? 1 : 0;
    }
}
