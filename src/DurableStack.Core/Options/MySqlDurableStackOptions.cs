namespace DurableStack.Core.Options;

/// <summary>
/// Settings for the MySQL storage provider, applied when
/// <c>DurableStackOptions.StorageProvider</c> is <see cref="DurableStackStorageProvider.MySql"/>.
/// </summary>
public sealed class MySqlDurableStackOptions
{
    /// <summary>
    /// MySQL connection string for the database that holds DurableStack's run and schedule
    /// tables. Empty by default; required when the MySQL provider is selected.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
