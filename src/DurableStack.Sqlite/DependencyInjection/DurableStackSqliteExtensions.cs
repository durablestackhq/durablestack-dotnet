using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Sqlite.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Sqlite.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the SQLite storage provider.
/// </summary>
public static class DurableStackSqliteExtensions
{
    /// <summary>
    /// Registers the SQLite storage provider: a singleton <see cref="SqliteJobStore"/>
    /// exposed as <see cref="IDurableJobStore"/>, and
    /// <see cref="SqliteDurableStackStoreMigrator"/> as the
    /// <see cref="IDurableStackStoreMigrator"/>. Requires a SQLite connection string in
    /// <see cref="DurableStackOptions.Sqlite"/>.
    /// </summary>
    public static IServiceCollection AddDurableStackSqliteStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new SqliteJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<SqliteJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, SqliteDurableStackStoreMigrator>();
        return services;
    }
}
