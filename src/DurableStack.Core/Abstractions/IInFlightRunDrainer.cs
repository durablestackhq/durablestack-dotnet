using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IInFlightRunDrainer
{
    Task DrainInFlightRunsAsync(CancellationToken cancellationToken);
}
