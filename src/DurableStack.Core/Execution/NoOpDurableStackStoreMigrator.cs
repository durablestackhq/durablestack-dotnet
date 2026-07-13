using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Execution;

/// <summary>
/// Migrator that does nothing, used for stores that need no schema setup, such as
/// <see cref="InMemoryJobStore"/>.
/// </summary>
public sealed class NoOpDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
