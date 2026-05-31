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
    return Results.Ok(new { count = jobs.Count, items = jobs });
});

app.MapPost("/schedules/{jobName}/disable", async (string jobName, IDurableScheduleAdminService schedules, CancellationToken ct) =>
{
    var updated = await schedules.SetScheduledJobEnabledAsync(jobName, enabled: false, ct);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapPost("/schedules/{jobName}/run-now", async (string jobName, IDurableScheduleAdminService schedules, CancellationToken ct) =>
{
    var runId = await schedules.RunScheduledJobNowAsync(jobName, ct);
    return runId.HasValue
        ? Results.Accepted($"/runs/{runId}", new { jobName, runId })
        : Results.NotFound();
});
```

## Run history filters

`IDurableJobRunQueryService` also supports schedule-focused history queries:

- `GetRunsByJobNameAsync(jobName, take)`
- `GetEnqueuedRunsAsync(take)` (excludes recurring-materialized runs)

## Endpoint contract notes (Postgres testbed)

- `GET /schedules`
  - returns `{ count, items }`
  - disabled schedules return `nextRunAtUtc = null`
- `POST /schedules/{jobName}/disable`
  - returns `404` if schedule is unknown
- `POST /schedules/{jobName}/enable`
  - recomputes `nextRunAtUtc` from current time
- `POST /schedules/{jobName}/run-now`
  - enqueues an ad-hoc run (does not mutate cron schedule)
  - returns `202` with `runId`
- `PUT /schedules/{jobName}/cron?cron=<expr>&timeZone=<iana>`
  - validates cron/time zone
  - returns `400` when invalid

## End-to-end operations flow

1. `GET /schedules`
2. `POST /schedules/heartbeat-every-minute/disable`
3. `GET /schedules` (confirm disabled)
4. `POST /schedules/heartbeat-every-minute/run-now`
5. `GET /runs/job/heartbeat-every-minute`
6. `PUT /schedules/heartbeat-every-minute/cron?cron=*/5 * * * *&timeZone=UTC`
7. `POST /schedules/heartbeat-every-minute/enable`
8. `GET /schedules` (confirm next run is populated)
