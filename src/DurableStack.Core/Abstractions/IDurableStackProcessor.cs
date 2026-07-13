using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// The worker's per-poll unit of work. Each cycle prunes old runs when a retention sweep is
/// due, materializes due recurring schedules into runs, then claims and starts executing due
/// runs up to the configured concurrency limit. The hosted worker loop calls this once per
/// poll interval.
/// </summary>
public interface IDurableStackProcessor
{
    /// <summary>
    /// Runs one processing cycle and returns the number of runs claimed. Claimed runs execute
    /// in the background; the returned count only reflects claiming, not completion. Returns 0
    /// when nothing was due or all execution slots were occupied.
    /// </summary>
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken);
}
