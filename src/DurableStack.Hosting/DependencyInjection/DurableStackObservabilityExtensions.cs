using DurableStack.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Hosting.DependencyInjection;

/// <summary>
/// Extension methods that wire DurableStack diagnostics into OpenTelemetry.
/// </summary>
public static class DurableStackObservabilityExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing and metrics for DurableStack, subscribing to the library's
    /// <see cref="System.Diagnostics.ActivitySource"/> and meter so worker polls, job executions,
    /// and retry counters are exported alongside the application's other telemetry.
    /// </summary>
    /// <param name="services">The service collection to add the OpenTelemetry registrations to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDurableStackOpenTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(DurableStackTelemetry.ActivitySourceName);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(DurableStackTelemetry.MeterName);
            });

        return services;
    }
}
