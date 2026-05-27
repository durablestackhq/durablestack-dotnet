using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.SqlServer.Storage;

public sealed class SqlServerDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly SqlServerJobStore _store;

    public SqlServerDurableStackStoreMigrator(SqlServerJobStore store)
    {
        _store = store;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
