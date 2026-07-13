using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Postgres.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Postgres.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the PostgreSQL storage provider.
/// </summary>
public static class DurableStackPostgresExtensions
{
    /// <summary>
    /// Registers the PostgreSQL storage provider: a singleton <see cref="PostgresJobStore"/>
    /// exposed as <see cref="IDurableJobStore"/>, and
    /// <see cref="PostgresDurableStackStoreMigrator"/> as the
    /// <see cref="IDurableStackStoreMigrator"/>. Requires a PostgreSQL connection string in
    /// <see cref="DurableStackOptions.Postgres"/>.
    /// </summary>
    public static IServiceCollection AddDurableStackPostgresStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new PostgresJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<PostgresJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, PostgresDurableStackStoreMigrator>();
        return services;
    }
}
