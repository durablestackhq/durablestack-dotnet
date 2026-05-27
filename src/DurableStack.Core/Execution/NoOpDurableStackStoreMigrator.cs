using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Execution;

public sealed class NoOpDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
