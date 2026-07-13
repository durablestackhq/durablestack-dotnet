using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.SqlServer.Storage;

/// <summary>
/// Applies the SQL Server schema migrations by delegating to
/// <see cref="SqlServerJobStore.EnsureMigrationsAppliedAsync"/>, which runs versioned,
/// transactional DDL under sp_getapplock.
/// </summary>
public sealed class SqlServerDurableStackStoreMigrator : IDurableStackStoreMigrator
{
    private readonly SqlServerJobStore _store;

    /// <summary>
    /// Creates a migrator that applies migrations through the given store.
    /// </summary>
    public SqlServerDurableStackStoreMigrator(SqlServerJobStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return _store.EnsureMigrationsAppliedAsync(cancellationToken);
    }
}
