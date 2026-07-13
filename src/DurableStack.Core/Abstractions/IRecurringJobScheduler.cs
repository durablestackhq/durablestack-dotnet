using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Turns due recurring schedules into concrete job runs. Materialization is guarded by
/// optimistic concurrency on the schedule's next-run time, so each cron slot produces at
/// most one run even when multiple workers poll simultaneously.
/// </summary>
public interface IRecurringJobScheduler
{
    /// <summary>
    /// Materializes every schedule whose next-run time has passed and returns the number of
    /// runs created. Slots another worker already materialized, schedules blocked by an
    /// active non-concurrent run, and (under the default SkipMissed catch-up policy) slots
    /// missed while no worker was running are counted as zero. A failure in one schedule is
    /// logged and does not stop the others.
    /// </summary>
    Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken);
}
