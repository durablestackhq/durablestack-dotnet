namespace DurableStack.Core.Options;

/// <summary>
/// Settings for the SQLite storage provider, applied when
/// <c>DurableStackOptions.StorageProvider</c> is <see cref="DurableStackStorageProvider.Sqlite"/>.
/// </summary>
public sealed class SqliteDurableStackOptions
{
    /// <summary>
    /// SQLite connection string (e.g. "Data Source=durablestack.db") for the file that holds
    /// DurableStack's run and schedule tables. Empty by default; required when the SQLite
    /// provider is selected.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
