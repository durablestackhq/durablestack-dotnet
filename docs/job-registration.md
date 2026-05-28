# Job Registration

DurableStack supports two job registration styles:

1. **Automatic discovery (default)**
2. **Explicit registration (power-user mode)**

## Automatic discovery (default)

When you call `AddDurableStack(...)`, DurableStack scans your app assembly and auto-registers public job classes:

- `IDurableJob`
- `IDurableJob<TArgs>`

Defaults:

- Job name defaults to class name
- Max attempts defaults to `3`
- Jobs are enqueue-only unless `[RecurringJob]` is present

Example:

```csharp
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;

[DurableJob(Name = "worker-heartbeat")]
[RecurringJob("* * * * *", TimeZone = "UTC")]
public sealed class WorkerHeartbeatJob : IDurableJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

## Discovery API

You can trigger scanning explicitly with one API family:

- `AddDurableJobsFromAssembly()`
- `AddDurableJobsFromAssembly<TMarker>()`
- `AddDurableJobsFromAssembly(Assembly assembly)`

The no-arg overload uses the entry assembly.

## Explicit registration (power-user mode)

Disable auto-discovery and register jobs manually when you need central runtime wiring or non-attribute configuration:

```csharp
builder.Services
    .AddDurableStack(options =>
    {
        options.JobRegistration.AutoDiscoverJobsFromAssembly = false;
    })
    .AddDurableJob<WorkerHeartbeatJob>("worker-heartbeat", job =>
    {
        job.RunOnCron("* * * * *", timeZone: "UTC");
        job.WithMaxAttempts(3);
    });
```

## Why both exist

- Auto-discovery minimizes boilerplate for most apps.
- Explicit registration gives maximal control for advanced setups.
