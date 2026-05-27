# Events

DurableStack emits lifecycle events through `IDurableStackEventSink`.

By default, DurableStack registers `NoOpDurableStackEventSink` (events are produced but dropped).

When `DurableStack:Eventing:TenantId` and `DurableStack:Eventing:ClientSecret` are configured,
DurableStack also auto-registers the API ingestion sink and background sync service.

This ingestion path is additive: custom sinks and logging sinks can be registered in parallel,
and event delivery fan-outs to all registered sinks.

## Event contract

Every event is represented by `DurableStackEvent`.

Key fields:

- `EventId` unique identifier for the event record
- `EventType` lifecycle type (see below)
- `EventVersion` event schema version (`DurableStackEventTypes.CurrentVersion`)
- `OccurredAtUtc` event timestamp
- `RunId`, `JobName`, `Attempt` run context when applicable
- `WorkerName` claiming/processing worker identity
- `TenantId`, `Environment`, `ServiceName` deployment metadata
- `TraceId`, `SpanId` current activity correlation when available
- `DurationMs` execution duration for completion/failure events when available
- `RetryAtUtc` retry schedule timestamp for retry-aware events
- `ErrorType`, `ErrorDetail` failure metadata
- `Message` optional payload (error message or retry timestamp)

## Emitted event types

- `job_claimed` run was claimed by a worker
- `job_started` handler execution started
- `job_succeeded` handler execution completed successfully
- `job_failed` handler execution failed
- `retry_scheduled` failed run was rescheduled for retry (`Message` contains next retry UTC ISO-8601 timestamp)
- `worker_heartbeat` worker loop heartbeat signal

## Payload expansion (version 2)

`EventVersion` is now `2`.

New payload fields:

- `DurationMs` duration in milliseconds for completed or failed job execution
- `RetryAtUtc` normalized retry timestamp for `job_failed` and `retry_scheduled` when retry applies
- `ErrorType` exception type for failures
- `ErrorDetail` full exception detail for failures

## Using the built-in logging sink

You can surface events directly to standard application logs.

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStack(builder.Configuration);
builder.Services.UseDurableStackLoggingEventSink();
```

This does not disable API ingestion when tenant credentials are configured.

## Using a custom sink

Implement `IDurableStackEventSink` and register it.

```csharp
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;

public sealed class MyEventSink : IDurableStackEventSink
{
    public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
    {
        // Send to OpenTelemetry, Kafka, Event Hub, etc.
        return Task.CompletedTask;
    }
}

builder.Services.AddDurableStack(builder.Configuration);
builder.Services.UseDurableStackEventSink<MyEventSink>();
```

## Notes

- event publishing is synchronous in the current worker loop path; sinks should avoid long blocking operations
- event schema additions should bump `EventVersion` and keep backwards compatibility where possible

## OpenTelemetry hooks

DurableStack exposes tracing and metrics via OpenTelemetry.

Enable registration:

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStack(builder.Configuration);
builder.Services.AddDurableStackOpenTelemetry();
```

Tracing:

- activity source: `DurableStack`
- span name: `durablestack.job.execute`
- tags: `durablestack.run_id`, `durablestack.job_name`, `durablestack.attempt`, `durablestack.worker_name`

Metrics (meter `DurableStack`):

- `durablestack.worker.polls`
- `durablestack.worker.heartbeats`
- `durablestack.jobs.claimed`
- `durablestack.jobs.started`
- `durablestack.jobs.succeeded`
- `durablestack.jobs.failed`
- `durablestack.jobs.retried`
- `durablestack.recurring.materialized`
- `durablestack.leases.extended`
