using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.MySql.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.MySql.DependencyInjection;

public static class DurableStackMySqlExtensions
{
    public static IServiceCollection AddDurableStackMySqlStore(this IServiceCollection services, DurableStackOptions options)
    {
        services.AddSingleton(provider => new MySqlJobStore(options));
        services.AddSingleton<IDurableJobStore>(provider => provider.GetRequiredService<MySqlJobStore>());
        services.AddSingleton<IDurableStackStoreMigrator, MySqlDurableStackStoreMigrator>();
        return services;
    }
}
