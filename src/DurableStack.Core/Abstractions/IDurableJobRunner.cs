using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

public interface IDurableJobRunner
{
    Task RunAsync(JobRunRecord run, CancellationToken cancellationToken);
}
