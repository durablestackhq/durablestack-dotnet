using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Sqlite.Storage;

/// <summary>
/// Applies the SQLite schema migrations by delegating to
/// <see cref="SqliteJobStore.EnsureMigrationsAppliedAsync"/>, which runs versioned,
/// transactional DDL serialized by SQLite's single-writer transaction.
/// </summary>
public sealed class SqliteDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly SqliteJobStore _store;

    /// <summary>
    /// Creates a migrator that applies migrations through the given store.
    /// </summary>
    public SqliteDurableStackStoreMigrator(SqliteJobStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
