using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Events;

/// <summary>
/// Event sink that discards every event. Registered as the default so the pipeline can
/// always publish without checking whether eventing is configured.
/// </summary>
public sealed class NoOpDurableStackEventSink : IDurableStackEventSink
{
    /// <inheritdoc />
    public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
