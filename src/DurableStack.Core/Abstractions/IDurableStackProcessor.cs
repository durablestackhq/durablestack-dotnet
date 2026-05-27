using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IDurableStackProcessor
{
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken);
}
