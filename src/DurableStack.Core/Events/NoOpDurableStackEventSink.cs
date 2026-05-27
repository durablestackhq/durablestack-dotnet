using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Events;

public sealed class NoOpDurableStackEventSink : IDurableStackEventSink
{
    public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
