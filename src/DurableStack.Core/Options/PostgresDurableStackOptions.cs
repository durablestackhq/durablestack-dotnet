namespace DurableStack.Core.Options;

/// <summary>
/// Settings for the PostgreSQL storage provider, applied when
/// <c>DurableStackOptions.StorageProvider</c> is <see cref="DurableStackStorageProvider.Postgres"/>.
/// </summary>
public sealed class PostgresDurableStackOptions
{
    /// <summary>
    /// Npgsql connection string for the database that holds DurableStack's run and schedule
    /// tables. Empty by default; required when the Postgres provider is selected.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
