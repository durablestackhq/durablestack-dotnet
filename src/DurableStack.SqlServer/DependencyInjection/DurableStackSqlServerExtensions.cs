using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.SqlServer.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.SqlServer.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the SQL Server storage provider.
/// </summary>
public static class DurableStackSqlServerExtensions
{
    /// <summary>
    /// Registers the SQL Server storage provider: a singleton <see cref="SqlServerJobStore"/>
    /// exposed as <see cref="IDurableJobStore"/>, and
    /// <see cref="SqlServerDurableStackStoreMigrator"/> as the
    /// <see cref="IDurableStackStoreMigrator"/>. Requires a SQL Server connection string in
    /// <see cref="DurableStackOptions.SqlServer"/>.
    /// </summary>
    public static IServiceCollection AddDurableStackSqlServerStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new SqlServerJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<SqlServerJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, SqlServerDurableStackStoreMigrator>();
        return services;
    }
}
