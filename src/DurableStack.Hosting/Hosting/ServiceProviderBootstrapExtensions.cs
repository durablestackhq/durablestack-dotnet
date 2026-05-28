using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Hosting.Hosting;

public static class ServiceProviderBootstrapExtensions
{
    public static async Task InitializeDurableStackAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var state = serviceProvider.GetService<DurableStackBootstrapState>();
        if (state is null)
        {
            await InitializeCoreAsync(serviceProvider, cancellationToken);
            return;
        }

        await state.InitializeOnceAsync(
            ct => InitializeCoreAsync(serviceProvider, ct),
            cancellationToken);
    }

    private static async Task InitializeCoreAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var migrator = serviceProvider.GetRequiredService<IDurableStackStoreMigrator>();
        var recurringInitializer = serviceProvider.GetRequiredService<IRecurringJobInitializer>();

        await migrator.MigrateAsync(cancellationToken);
        await recurringInitializer.InitializeAsync(cancellationToken);
    }
}
