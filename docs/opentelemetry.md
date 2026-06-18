# OpenTelemetry with DurableStack

This guide shows how to enable, export, and verify DurableStack telemetry using OpenTelemetry in .NET apps.

## What DurableStack emits

DurableStack exposes both tracing and metrics:

- Activity source: `DurableStack`
- Meter: `DurableStack`

Trace span:

- `durablestack.job.execute`

Common span tags:

- `durablestack.run_id`
- `durablestack.job_name`
- `durablestack.attempt`
- `durablestack.worker_name`

Metrics:

- `durablestack.worker.polls`
- `durablestack.worker.heartbeats`
- `durablestack.jobs.claimed`
- `durablestack.jobs.started`
- `durablestack.jobs.succeeded`
- `durablestack.jobs.failed`
- `durablestack.jobs.retried`
- `durablestack.recurring.materialized`
- `durablestack.leases.extended`

## 1) Register DurableStack and OpenTelemetry

```csharp
using DurableStack.Hosting.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDurableStack(builder.Configuration);
builder.Services.AddDurableStackOpenTelemetry();
```

`AddDurableStackOpenTelemetry()` wires DurableStack's activity source and meter into OpenTelemetry.

## 2) Add exporters

`AddDurableStackOpenTelemetry()` only registers instrumentation sources. You still need exporters.

Example with OTLP exporter:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddOtlpExporter();
    });
```

Or use environment variables with OTLP defaults:

- `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`
- `OTEL_SERVICE_NAME=my-app`

## 3) Configure resource identity (recommended)

Set service identity so traces/metrics are grouped correctly:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: "my-app",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName);
    });
```

## 4) Verify telemetry locally

Quick verification flow:

1. Start a local collector (or backend such as Jaeger/Tempo/Prometheus-compatible stack).
2. Start your app with DurableStack jobs enabled.
3. Enqueue or trigger a job.
4. Confirm:
   - trace `durablestack.job.execute` appears
   - DurableStack metrics are present

## 5) Kubernetes guidance

For multi-replica workers, pair telemetry with unique worker identity:

- set `DurableStackOptions.WorkerName` uniquely per pod/process
- include pod metadata in OTel resource attributes when possible

Useful attributes:

- `service.name`
- `service.instance.id`
- `deployment.environment`
- `k8s.pod.name`
- `k8s.namespace.name`

## Troubleshooting

If no DurableStack telemetry appears:

1. Confirm `AddDurableStackOpenTelemetry()` is called.
2. Confirm exporters are configured (OTLP/console/etc).
3. Confirm jobs are actually running or being claimed.
4. Check collector endpoint and network access.
5. Ensure sampling is not dropping traces unexpectedly.

## Testing status

DurableStack includes automated tests for OpenTelemetry hooks:

- activity emission for `durablestack.job.execute`
- lifecycle metric counters from processor loop
- lease extension metric from heartbeat runner
- worker heartbeat metric from hosted loop

See `src/DurableStack.Tests/OpenTelemetryHooksTests.cs`.
