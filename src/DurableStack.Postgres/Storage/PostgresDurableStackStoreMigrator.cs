using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Postgres.Storage;

public sealed class PostgresDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly PostgresJobStore _store;

    public PostgresDurableStackStoreMigrator(PostgresJobStore store)
    {
        _store = store;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
