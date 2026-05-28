# Scheduled Job Management

DurableStack exposes `IDurableScheduleAdminService` for recurring schedule administration at runtime.

## Supported operations

- list all recurring schedules
- disable / re-enable a schedule
- enqueue a scheduled job immediately (`run now`)
- update cron expression and time zone

## Service usage

```csharp
app.MapGet("/schedules", async (IDurableScheduleAdminService schedules, CancellationToken ct) =>
{
    var jobs = await schedules.ListScheduledJobsAsync(includeDisabled: true, ct);
    return Results.Ok(jobs);
});

app.MapPost("/schedules/{jobName}/disable", async (string jobName, IDurableScheduleAdminService schedules, CancellationToken ct) =>
{
    var updated = await schedules.SetScheduledJobEnabledAsync(jobName, enabled: false, ct);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapPost("/schedules/{jobName}/run-now", async (string jobName, IDurableScheduleAdminService schedules, CancellationToken ct) =>
{
    var queued = await schedules.RunScheduledJobNowAsync(jobName, ct);
    return queued ? Results.Accepted() : Results.NotFound();
});
```

## Run history filters

`IDurableJobRunQueryService` also supports schedule-focused history queries:

- `GetRunsByJobNameAsync(jobName, take)`
- `GetEnqueuedRunsAsync(take)` (excludes recurring-materialized runs)
