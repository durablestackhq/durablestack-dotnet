namespace DurableStack.Core.Options;

/// <summary>
/// The backing store that persists job runs and recurring schedules. All database
/// providers give the same durability and lease-fencing guarantees; the in-memory provider
/// does not survive process exit.
/// </summary>
public enum DurableStackStorageProvider
{
    /// <summary>
    /// Process-local store with no durability: all runs and schedules are lost on restart,
    /// and leases cannot coordinate across processes. Intended for tests and local
    /// development. This is the default.
    /// </summary>
    InMemory = 0,

    /// <summary>
    /// PostgreSQL-backed store, configured via <c>DurableStackOptions.Postgres</c>.
    /// </summary>
    Postgres = 1,

    /// <summary>
    /// SQL Server-backed store, configured via <c>DurableStackOptions.SqlServer</c>.
    /// </summary>
    SqlServer = 2,

    /// <summary>
    /// SQLite-backed store, configured via <c>DurableStackOptions.Sqlite</c>. Durable but
    /// file-based; suited to single-node deployments.
    /// </summary>
    Sqlite = 3,

    /// <summary>
    /// MySQL-backed store, configured via <c>DurableStackOptions.MySql</c>.
    /// </summary>
    MySql = 4,
}
