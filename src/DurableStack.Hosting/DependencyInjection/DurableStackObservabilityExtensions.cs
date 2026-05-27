using DurableStack.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Hosting.DependencyInjection;

public static class DurableStackObservabilityExtensions
{
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
