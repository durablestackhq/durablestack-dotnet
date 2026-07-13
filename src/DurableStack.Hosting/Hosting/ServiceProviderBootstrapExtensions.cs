using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Hosting.Hosting;

/// <summary>
/// Extension methods for initializing DurableStack storage ahead of host start.
/// </summary>
public static class ServiceProviderBootstrapExtensions
{
    /// <summary>
    /// Applies job-store migrations and syncs recurring job registrations before the host (and its
    /// hosted services) start — useful when other startup code needs the DurableStack schema to exist,
    /// or when enqueuing jobs before the worker runs. Initialization is tracked via
    /// <c>DurableStackBootstrapState</c> so repeated calls execute the work at most once; the steps
    /// themselves are idempotent and safe to run again when the worker hosted service starts.
    /// </summary>
    /// <param name="serviceProvider">Service provider containing the DurableStack registrations.</param>
    /// <param name="cancellationToken">Cancels the migration and sync work.</param>
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
