using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Postgres.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Postgres.DependencyInjection;

public static class DurableStackPostgresExtensions
{
    public static IServiceCollection AddDurableStackPostgresStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new PostgresJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<PostgresJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, PostgresDurableStackStoreMigrator>();
        return services;
    }
}
