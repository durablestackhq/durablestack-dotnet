using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;

namespace DurableStack.Core.Abstractions;

public interface IDurableJob
{
    Task ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}
