using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Sqlite.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Sqlite.DependencyInjection;

public static class DurableStackSqliteExtensions
{
    public static IServiceCollection AddDurableStackSqliteStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new SqliteJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<SqliteJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, SqliteDurableStackStoreMigrator>();
        return services;
    }
}
