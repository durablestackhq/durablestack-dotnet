using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;

namespace DurableStack.Core.Abstractions;

public interface IDurableJob<TArgs>
{
    Task ExecuteAsync(TArgs args, JobContext context, CancellationToken cancellationToken);
}
