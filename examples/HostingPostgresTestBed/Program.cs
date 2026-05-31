using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var workerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
var runStatuses = new[] { "pending", "leased", "succeeded", "failed" };

builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(
        Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"),
        optional: true,
        reloadOnChange: false);

// Required: register DurableStack + hosted background processing using PostgreSQL storage.
builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.ConnectionStringName = "DurableStack";
});
// Optional additional sink for local debugging:
// builder.Services.UseDurableStackLoggingEventSink();
// Optional: emit DurableStack traces/metrics via OpenTelemetry.
builder.Services.AddDurableStackOpenTelemetry();

builder.Logging.AddSimpleConsole();

var app = builder.Build();

app.Logger.LogInformation("DurableStack example started. Provider=Postgres WorkerName={WorkerName}", workerName);

app.MapGet("/", () => "DurableStack Hosting PostgreSQL Test Bed");

app.MapPost("/migrate", async (IDurableStackStoreMigrator migrator, CancellationToken cancellationToken) =>
{
    await migrator.MigrateAsync(cancellationToken);
    return Results.Ok(new { migrated = true });
});

app.MapPost("/enqueue", async (IDurableStackClient jobs, string email, CancellationToken cancellationToken) =>
{
    var args = new SendWelcomeEmailArgs
    {
        Email = email,
    };

    var runId = await jobs.EnqueueAsync<SendWelcomeEmailJob>(args, cancellationToken);
    return Results.Accepted($"/runs/{runId}", new { runId });
});

app.MapGet("/runs", async (IDurableJobRunQueryService query, CancellationToken cancellationToken) =>
{
    var runs = await query.GetRecentRunsAsync(50, cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/runs/job/{jobName}", async (
    string jobName,
    IDurableJobRunQueryService query,
    int? take,
    CancellationToken cancellationToken) =>
{
    var limit = Math.Clamp(take ?? 50, 1, 500);
    var runs = await query.GetRunsByJobNameAsync(jobName, limit, cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/runs/enqueued", async (
    IDurableJobRunQueryService query,
    int? take,
    CancellationToken cancellationToken) =>
{
    var limit = Math.Clamp(take ?? 50, 1, 500);
    var runs = await query.GetEnqueuedRunsAsync(limit, cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/runs/{id:guid}", async (Guid id, IDurableJobRunQueryService query, CancellationToken cancellationToken) =>
{
    var run = await query.GetRunAsync(id, cancellationToken);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

// Valid run statuses: pending, leased, succeeded, failed.
app.MapGet("/runs/status/{status}", async (
    string status,
    IDurableJobRunQueryService query,
    int? take,
    CancellationToken cancellationToken) =>
{
    var normalized = status.Trim().ToLowerInvariant();
    if (!runStatuses.Contains(normalized, StringComparer.Ordinal))
    {
        return Results.BadRequest(new
        {
            error = "Invalid status.",
            allowedStatuses = runStatuses,
        });
    }

    var limit = Math.Clamp(take ?? 50, 1, 500);
    var runs = await query.GetRunsByStatusAsync(normalized, limit, cancellationToken);
    return Results.Ok(runs);
});

app.MapPost("/enqueue-long-running", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var runId = await jobs.EnqueueAsync<LongRunningLeaseDemoJob>(cancellationToken: cancellationToken);
    return Results.Accepted($"/runs/{runId}", new { runId });
});

app.MapGet("/schedules", async (IDurableScheduleAdminService schedules, CancellationToken cancellationToken) =>
{
    var jobs = await schedules.ListScheduledJobsAsync(includeDisabled: true, cancellationToken);
    var response = jobs.Select(job => new
    {
        job.JobName,
        job.JobType,
        job.CronExpression,
        job.TimeZone,
        job.MaxAttempts,
        job.Enabled,
        nextRunAtUtc = job.Enabled && job.NextRunAtUtc > DateTimeOffset.MinValue ? job.NextRunAtUtc : (DateTimeOffset?)null,
    });

    return Results.Ok(new { count = jobs.Count, items = response });
});

app.MapPost("/schedules/{jobName}/disable", async (
    string jobName,
    IDurableScheduleAdminService schedules,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(jobName))
    {
        return Results.BadRequest(new { error = "jobName is required." });
    }

    var updated = await schedules.SetScheduledJobEnabledAsync(jobName, enabled: false, cancellationToken);
    return updated
        ? Results.Ok(new { jobName, enabled = false, action = "disabled" })
        : Results.NotFound(new { error = $"Schedule '{jobName}' was not found." });
});

app.MapPost("/schedules/{jobName}/enable", async (
    string jobName,
    IDurableScheduleAdminService schedules,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(jobName))
    {
        return Results.BadRequest(new { error = "jobName is required." });
    }

    var updated = await schedules.SetScheduledJobEnabledAsync(jobName, enabled: true, cancellationToken);
    return updated
        ? Results.Ok(new { jobName, enabled = true, action = "enabled" })
        : Results.NotFound(new { error = $"Schedule '{jobName}' was not found." });
});

app.MapPost("/schedules/{jobName}/run-now", async (
    string jobName,
    IDurableScheduleAdminService schedules,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(jobName))
    {
        return Results.BadRequest(new { error = "jobName is required." });
    }

    var runId = await schedules.RunScheduledJobNowAsync(jobName, cancellationToken);
    return runId.HasValue
        ? Results.Accepted($"/runs/{runId}", new { jobName, runId, action = "queued-now" })
        : Results.NotFound(new { error = $"Schedule '{jobName}' was not found." });
});

app.MapPut("/schedules/{jobName}/cron", async (
    string jobName,
    string cron,
    string? timeZone,
    IDurableScheduleAdminService schedules,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(jobName))
    {
        return Results.BadRequest(new { error = "jobName is required." });
    }

    if (string.IsNullOrWhiteSpace(cron))
    {
        return Results.BadRequest(new { error = "cron is required." });
    }

    var normalizedTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;
    bool updated;

    try
    {
        updated = await schedules.UpdateScheduledJobCronAsync(
            jobName,
            cron,
            normalizedTimeZone,
            cancellationToken);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = "Invalid cron or time zone.",
            detail = ex.Message,
        });
    }

    return updated
        ? Results.Ok(new { jobName, cron, timeZone = normalizedTimeZone, action = "schedule-updated" })
        : Results.NotFound(new { error = $"Schedule '{jobName}' was not found." });
});

app.MapPost("/enqueue-fail-always", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-always",
        FailUntilAttempt = 10,
    };

    var runId = await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted($"/runs/{runId}", new { runId });
});

app.MapPost("/enqueue-fail-once", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-once",
        FailUntilAttempt = 1,
    };

    var runId = await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted($"/runs/{runId}", new { runId });
});

app.MapPost("/enqueue-fail-twice", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-twice",
        FailUntilAttempt = 2,
    };

    var runId = await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted($"/runs/{runId}", new { runId });
});

app.MapPost("/enqueue-fail-custom", async (
    int? failUntilAttempt,
    int? maxAttempts,
    string? scenarioName,
    IDurableJobRegistry registry,
    IDurableJobStore store,
    CancellationToken cancellationToken) =>
{
    var registration = registry.FindByJobType(typeof(FlakyFailureDemoJob));
    if (registration is null)
    {
        return Results.Problem("flaky-failure-demo job is not registered.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var configuredMaxAttempts = maxAttempts.HasValue
        ? Math.Clamp(maxAttempts.Value, 1, 25)
        : registration.MaxAttempts;
    var effectiveFailUntilAttempt = Math.Clamp(failUntilAttempt ?? 1, 0, 100);
    var effectiveScenarioName = string.IsNullOrWhiteSpace(scenarioName)
        ? $"custom-fail-until-{effectiveFailUntilAttempt}-max-{configuredMaxAttempts}"
        : scenarioName.Trim();

    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = effectiveScenarioName,
        FailUntilAttempt = effectiveFailUntilAttempt,
    };

    var payloadJson = JsonSerializer.Serialize(args);
    var runId = await store.EnqueueAsync(
        registration.JobName,
        registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
        payloadJson,
        DateTimeOffset.UtcNow,
        configuredMaxAttempts,
        cancellationToken);

    return Results.Accepted($"/runs", new
    {
        runId,
        endpoint = "/enqueue-fail-custom",
        scenarioName = effectiveScenarioName,
        failUntilAttempt = effectiveFailUntilAttempt,
        maxAttempts = configuredMaxAttempts,
        note = "This endpoint enqueues flaky-failure-demo with per-run maxAttempts override."
    });
});

app.Run();
