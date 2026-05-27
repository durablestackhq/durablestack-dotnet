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

ASP.NET Core with PostgreSQL:

```csharp
using DurableStack.AspNetCore.DependencyInjection;

builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

Worker Service with PostgreSQL:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

ASP.NET Core with MySQL:

```csharp
using DurableStack.AspNetCore.DependencyInjection;

builder.Services.AddDurableStackMySql(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

Worker Service with MySQL:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackMySql(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

ASP.NET Core with SQL Server:

```csharp
using DurableStack.AspNetCore.DependencyInjection;

builder.Services.AddDurableStackSqlServer(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

Worker Service with SQL Server:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackSqlServer(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

ASP.NET Core with SQLite:

```csharp
using DurableStack.AspNetCore.DependencyInjection;

builder.Services.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

Worker Service with SQLite:

```csharp
using DurableStack.Worker.Hosting;

builder.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

Provider-agnostic mode from configuration:

```csharp
builder.Services.AddDurableStack(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

In this mode, `DurableStack:StorageProvider` selects the backing store and provider-specific connection options are read from configuration.

In-memory local development:

```csharp
builder.Services.AddDurableStack(options =>
{
    options.WorkerName = workerName;
});
```

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
using DurableStack.AspNetCore.DependencyInjection;
using DurableStack.Core.Options;

builder.Services
    .AddDurableStack(options =>
    {
        options.WorkerName = "api-worker";
        options.Recurring.CatchUpPolicy = RecurringCatchUpPolicy.SkipMissed;
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
