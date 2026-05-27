using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IRecurringJobInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
