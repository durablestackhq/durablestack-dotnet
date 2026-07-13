using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Executes a single claimed run: activates the registered job type, deserializes the
/// payload, and invokes the job. Decorators layer cross-cutting behavior here — the
/// default pipeline wraps execution with lease heartbeating so long runs keep their lease.
/// </summary>
public interface IDurableJobRunner
{
    /// <summary>
    /// Executes one attempt of the given claimed run. A thrown exception signals a failed
    /// attempt to the caller, which records it and schedules a retry while attempts remain.
    /// </summary>
    /// <param name="run">The run to execute; the caller must already hold its lease.</param>
    /// <param name="cancellationToken">Signaled when the hosting worker is shutting down.</param>
    Task RunAsync(JobRunRecord run, CancellationToken cancellationToken);
}
