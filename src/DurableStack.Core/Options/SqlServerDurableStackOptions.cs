namespace DurableStack.Core.Options;

/// <summary>
/// Settings for the SQL Server storage provider, applied when
/// <c>DurableStackOptions.StorageProvider</c> is <see cref="DurableStackStorageProvider.SqlServer"/>.
/// </summary>
public sealed class SqlServerDurableStackOptions
{
    /// <summary>
    /// SQL Server connection string for the database that holds DurableStack's run and
    /// schedule tables. Empty by default; required when the SQL Server provider is selected.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
