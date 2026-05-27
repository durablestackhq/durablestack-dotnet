using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Events;

namespace DurableStack.Core.Abstractions;

public interface IDurableStackEventSink
{
    Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default);
}
