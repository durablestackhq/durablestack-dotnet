using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IRecurringJobScheduler
{
    Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken);
}
