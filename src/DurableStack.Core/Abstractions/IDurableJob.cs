using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// A background job with no typed payload. Implementations are resolved from dependency
/// injection and executed by a worker under an at-least-once contract: a run may execute
/// more than once (for example after a lease expires mid-execution), so implementations
/// should be idempotent.
/// </summary>
public interface IDurableJob
{
    /// <summary>
    /// Executes one attempt of the job. Throwing marks the attempt as failed and schedules
    /// a retry while attempts remain; returning normally marks the run as succeeded.
    /// </summary>
    /// <param name="context">Run metadata (run id, job name, attempt number, scheduled time) and a scoped service provider.</param>
    /// <param name="cancellationToken">Signaled when the hosting worker is shutting down; the run is later reclaimed by another worker.</param>
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}
