using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Postgres.Storage;

/// <summary>
/// Applies the PostgreSQL schema migrations by delegating to
/// <see cref="PostgresJobStore.EnsureMigrationsAppliedAsync"/>, which runs versioned,
/// transactional DDL under a pg_advisory_xact_lock.
/// </summary>
public sealed class PostgresDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly PostgresJobStore _store;

    /// <summary>
    /// Creates a migrator that applies migrations through the given store.
    /// </summary>
    public PostgresDurableStackStoreMigrator(PostgresJobStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
