using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Sqlite.Storage;

public sealed class SqliteDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly SqliteJobStore _store;

    public SqliteDurableStackStoreMigrator(SqliteJobStore store)
    {
        _store = store;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
