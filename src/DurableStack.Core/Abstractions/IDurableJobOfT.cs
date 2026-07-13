using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// A background job that receives a strongly typed payload, deserialized from the JSON
/// stored with the run. Executed under the same at-least-once contract as
/// <see cref="IDurableJob"/>: a run may execute more than once, so implementations should
/// be idempotent.
/// </summary>
/// <typeparam name="TArgs">The payload type, deserialized from the run's stored JSON with System.Text.Json.</typeparam>
public interface IDurableJob<TArgs>
{
    /// <summary>
    /// Executes one attempt of the job. Throwing marks the attempt as failed and schedules
    /// a retry while attempts remain; returning normally marks the run as succeeded.
    /// </summary>
    /// <param name="args">The deserialized payload; default-valued when the run was enqueued without one.</param>
    /// <param name="context">Run metadata (run id, job name, attempt number, scheduled time) and a scoped service provider.</param>
    /// <param name="cancellationToken">Signaled when the hosting worker is shutting down; the run is later reclaimed by another worker.</param>
    Task ExecuteAsync(TArgs args, JobContext context, CancellationToken cancellationToken);
}
