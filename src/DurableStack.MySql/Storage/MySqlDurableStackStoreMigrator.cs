using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.MySql.Storage;

/// <summary>
/// Applies the MySQL schema migrations by delegating to
/// <see cref="MySqlJobStore.EnsureMigrationsAppliedAsync"/>, which runs versioned,
/// individually idempotent statements under GET_LOCK (MySQL DDL is not transactional).
/// </summary>
public sealed class MySqlDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly MySqlJobStore _store;

    /// <summary>
    /// Creates a migrator that applies migrations through the given store.
    /// </summary>
    public MySqlDurableStackStoreMigrator(MySqlJobStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
