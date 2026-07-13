using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Lets the hosted worker wait for in-flight job executions to finish during graceful
/// shutdown, bounded by <c>DurableStackOptions.ShutdownDrainTimeout</c>. Runs that do not
/// finish in time keep their lease and are reclaimed by another worker after it expires.
/// </summary>
public interface IInFlightRunDrainer
{
    /// <summary>
    /// Completes when every run currently executing on this worker has finished, or throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is
    /// signaled first.
    /// </summary>
    Task DrainInFlightRunsAsync(CancellationToken cancellationToken);
}
