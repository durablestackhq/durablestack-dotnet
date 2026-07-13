using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.MySql.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.MySql.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the MySQL storage provider.
/// </summary>
public static class DurableStackMySqlExtensions
{
    /// <summary>
    /// Registers the MySQL storage provider: a singleton <see cref="MySqlJobStore"/>
    /// exposed as <see cref="IDurableJobStore"/>, and
    /// <see cref="MySqlDurableStackStoreMigrator"/> as the
    /// <see cref="IDurableStackStoreMigrator"/>. Requires a MySQL connection string in
    /// <see cref="DurableStackOptions.MySql"/>.
    /// </summary>
    public static IServiceCollection AddDurableStackMySqlStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new MySqlJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<MySqlJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, MySqlDurableStackStoreMigrator>();
        return services;
    }
}
