using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Creates or upgrades the database schema (tables and indexes) a storage provider needs
/// before the worker starts processing. The in-memory provider uses a no-op implementation.
/// </summary>
public interface IDurableStackStoreMigrator
{
    /// <summary>
    /// Applies any pending schema migrations. Safe to call on every startup; it does nothing
    /// when the schema is already current.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
