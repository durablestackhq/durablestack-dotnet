using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);
var workerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
var runStatuses = new[] { "pending", "leased", "succeeded", "failed" };

builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(
        Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"),
        optional: true,
        reloadOnChange: false);

// Required: register DurableStack + hosted background processing using SQLite storage.
builder.Services.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.ConnectionStringName = "DurableStack";
});
// Uncomment to surface DurableStack lifecycle events (including worker heartbeats) in logs.
// builder.Services.UseDurableStackLoggingEventSink();
// Optional: emit DurableStack traces/metrics via OpenTelemetry.
builder.Services.AddDurableStackOpenTelemetry();

builder.Logging.AddSimpleConsole();

var app = builder.Build();

app.Logger.LogInformation("DurableStack example started. Provider=Sqlite WorkerName={WorkerName}", workerName);

app.MapGet("/", () => "DurableStack Hosting SQLite Example");

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

app.Run();
