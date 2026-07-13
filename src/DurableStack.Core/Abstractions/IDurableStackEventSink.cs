using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Events;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Receives lifecycle events (claimed, started, succeeded, failed, retried) emitted by the
/// worker. Sinks are observational only: exceptions thrown here are logged and swallowed,
/// and never affect the outcome of the run that produced the event.
/// </summary>
public interface IDurableStackEventSink
{
    /// <summary>
    /// Publishes one lifecycle event. Implementations should return quickly (buffer and
    /// flush in the background if needed) because publishing happens on the worker's
    /// execution path.
    /// </summary>
    Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default);
}
