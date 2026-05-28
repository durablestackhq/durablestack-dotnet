# Getting Started

DurableStack supports one-off and recurring durable jobs.

## Worker identity recommendation

For distributed environments, use a unique worker name per process or container.

```csharp
var workerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";

builder.Services.AddDurableStack(options =>
{
    options.WorkerName = workerName;
});
```

## Preferred setup styles

Durable providers are opt-in. In-memory is the default and is useful for local development.

Job registration is auto-discovered by default from the app assembly. Any public class implementing `IDurableJob` or `IDurableJob<TArgs>` is registered automatically.

- Default job name: class name
- Default max attempts: `3`
- Add `[RecurringJob("...")]` to make a job scheduled
- Without `[RecurringJob]`, a job is enqueue-only

ASP.NET Core with PostgreSQL:

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

Worker Service with PostgreSQL:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

ASP.NET Core with MySQL:

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStackMySql(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

Worker Service with MySQL:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackMySql(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

ASP.NET Core with SQL Server:

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStackSqlServer(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

Worker Service with SQL Server:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackSqlServer(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

ASP.NET Core with SQLite:

```csharp
using DurableStack.Hosting.DependencyInjection;

builder.Services.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

Worker Service with SQLite:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

Provider-agnostic mode from configuration:

```csharp
builder.Services.AddDurableStack(builder.Configuration, options =>
{
    options.WorkerName = workerName;
});
```

In this mode, `DurableStack:StorageProvider` selects the backing store and provider-specific connection options are read from configuration.

If your host app uses a non-default connection string name, set `ConnectionStringName`:

```csharp
builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.ConnectionStringName = "acmewidgets_prod";
});
```

Optional worker tuning can be set in configuration:

```json
{
  "DurableStack": {
    "PollIntervalSeconds": 0.5,
    "BatchSize": 25,
    "LeaseDurationSeconds": 5
  }
}
```

If these values are omitted, DurableStack uses defaults: `PollInterval=5s`, `BatchSize=50`, `LeaseDuration=30s`.

## Data retention

DurableStack can automatically prune old terminal runs (`succeeded` and `failed`) to prevent unbounded growth.

Default retention windows:

- In-memory: `1 hour`
- Database providers: `24 hours`

Default cleanup cadence:

- Sweep interval: `5 minutes`
- Delete batch size: `1000`

Configuration example:

```json
{
  "DurableStack": {
    "Retention": {
      "Enabled": true,
      "RunRetentionSeconds": 86400,
      "SweepIntervalSeconds": 300,
      "DeleteBatchSize": 1000
    }
  }
}
```

In-memory local development:

```csharp
builder.Services.AddDurableStack(options =>
{
    options.WorkerName = workerName;
});
```

## Non-hosted bootstrap (manual loop apps)

If you are not running `DurableStackHostedService` (for example, in a manual console loop), initialize DurableStack once through the service provider:

```csharp
using DurableStack.Hosting.Hosting;

using var provider = services.BuildServiceProvider();
await provider.InitializeDurableStackAsync(CancellationToken.None);
```

This runs store migration + recurring schedule initialization. In hosted ASP.NET Core/worker scenarios, this is already handled automatically by `DurableStackHostedService`.

## Eventing credentials: config or explicit init

You can provide DurableStack eventing credentials either through configuration or directly in the init callback.

When you use `AddDurableStack*(builder.Configuration, options => ...)`, DurableStack first binds `DurableStack` from configuration, then applies your callback overrides.

Configuration example (`appsettings.json` or `appsettings.Development.json`):

```json
{
  "DurableStack": {
    "Eventing": {
      "TenantId": "tenant_...",
      "ClientSecret": "secret_...",
      "IngestionApiBaseUrl": "https://localhost:7163"
    }
  }
}
```

Explicit init example (overrides config values when both are present):

```csharp
builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.Eventing.TenantId = tenantId;
    options.Eventing.ClientSecret = clientSecret;
    options.Eventing.IngestionApiBaseUrl = ingestionApiBaseUrl;
});
```

If both `TenantId` and `ClientSecret` are set, API ingestion is enabled automatically.

## Recurring job example

Use IANA time zone IDs for recurring schedules (for example, `America/Chicago` or `UTC`).

```csharp
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Options;

builder.Services
    .AddDurableStack(options =>
    {
        options.WorkerName = "api-worker";
        options.Recurring.CatchUpPolicy = RecurringCatchUpPolicy.SkipMissed;
    });

[DurableJob(Name = "worker-heartbeat")]
[RecurringJob("*/5 * * * *", TimeZone = "America/Chicago")]
public sealed class RecurringWorkerHeartbeatJob : IDurableJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

## Explicit registration (power-user mode)

If you want full manual control, disable auto-discovery and register jobs explicitly:

```csharp
builder.Services
    .AddDurableStack(options =>
    {
        options.JobRegistration.AutoDiscoverJobsFromAssembly = false;
    })
    .AddDurableJob<RecurringWorkerHeartbeatJob>("worker-heartbeat", job =>
    {
        job.RunOnCron("*/5 * * * *", timeZone: "America/Chicago");
        job.WithMaxAttempts(3);
    });
```

For timezone guidance and Windows/IANA mapping, see `docs/timezones.md`.

Run query endpoints in example APIs:

- `GET /runs`
- `GET /runs/{id}`
- `GET /runs/status/{status}?take=50` where status is `pending`, `leased`, `succeeded`, or `failed`
