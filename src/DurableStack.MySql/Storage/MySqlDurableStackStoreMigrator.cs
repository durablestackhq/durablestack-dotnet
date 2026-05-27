using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.MySql.Storage;

public sealed class MySqlDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly MySqlJobStore _store;

    public MySqlDurableStackStoreMigrator(MySqlJobStore store)
    {
        _store = store;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
