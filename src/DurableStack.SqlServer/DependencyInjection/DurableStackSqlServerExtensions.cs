using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.SqlServer.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.SqlServer.DependencyInjection;

public static class DurableStackSqlServerExtensions
{
    public static IServiceCollection AddDurableStackSqlServerStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new SqlServerJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<SqlServerJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, SqlServerDurableStackStoreMigrator>();
        return services;
    }
}
