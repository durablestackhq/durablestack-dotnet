using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core;
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

builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(5);
});
// Optional additional sink for local debugging:
// builder.Services.UseDurableStackLoggingEventSink();
builder.Services.AddDurableStackOpenTelemetry();

builder.Logging.AddSimpleConsole();

builder.Services.AddDurableJob<SendWelcomeEmailJob, SendWelcomeEmailArgs>("send-welcome-email", job =>
{
    job.WithMaxAttempts(3);
});
builder.Services.AddDurableJob<HeartbeatJob>("heartbeat-every-minute", job =>
{
    job.RunOnCron("* * * * *", timeZone: "UTC");
    job.WithMaxAttempts(3);
});
builder.Services.AddDurableJob<LongRunningLeaseDemoJob>("long-running-lease-demo", job =>
{
    job.WithMaxAttempts(3);
});
builder.Services.AddDurableJob<FlakyFailureDemoJob, FlakyFailureDemoArgs>("flaky-failure-demo", job =>
{
    job.WithMaxAttempts(5);
});

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

    await jobs.EnqueueAsync<SendWelcomeEmailJob>(args, cancellationToken);
    return Results.Accepted();
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
    await jobs.EnqueueAsync<LongRunningLeaseDemoJob>(cancellationToken: cancellationToken);
    return Results.Accepted();
});

app.MapPost("/enqueue-fail-always", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-always",
        FailUntilAttempt = 10,
    };

    await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/enqueue-fail-once", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-once",
        FailUntilAttempt = 1,
    };

    await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/enqueue-fail-twice", async (IDurableStackClient jobs, CancellationToken cancellationToken) =>
{
    var args = new FlakyFailureDemoArgs
    {
        ScenarioName = "fail-twice",
        FailUntilAttempt = 2,
    };

    await jobs.EnqueueAsync<FlakyFailureDemoJob>(args, cancellationToken);
    return Results.Accepted();
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

public sealed class SendWelcomeEmailJob : IDurableJob<SendWelcomeEmailArgs>
{
    private readonly ILogger<SendWelcomeEmailJob> _logger;

    public SendWelcomeEmailJob(ILogger<SendWelcomeEmailJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(SendWelcomeEmailArgs args, JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[PostgresTestBed] Sending welcome email to {Email}. RunId={RunId} Attempt={Attempt}",
            args.Email,
            context.RunId,
            context.Attempt);

        return Task.CompletedTask;
    }
}

public sealed class SendWelcomeEmailArgs
{
    public string Email { get; set; } = string.Empty;
}

public sealed class HeartbeatJob : IDurableJob
{
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PostgresTestBed] Recurring heartbeat executed. RunId={RunId}", context.RunId);
        return Task.CompletedTask;
    }
}

public sealed class LongRunningLeaseDemoJob : IDurableJob
{
    private readonly ILogger<LongRunningLeaseDemoJob> _logger;

    public LongRunningLeaseDemoJob(ILogger<LongRunningLeaseDemoJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[PostgresTestBed] Long-running lease demo started. RunId={RunId}", context.RunId);
        await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        _logger.LogInformation("[PostgresTestBed] Long-running lease demo completed. RunId={RunId}", context.RunId);
    }
}

public sealed class FlakyFailureDemoJob : IDurableJob<FlakyFailureDemoArgs>
{
    private readonly ILogger<FlakyFailureDemoJob> _logger;

    public FlakyFailureDemoJob(ILogger<FlakyFailureDemoJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(FlakyFailureDemoArgs args, JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[PostgresTestBed] Flaky failure demo executing. Scenario={ScenarioName} RunId={RunId} Attempt={Attempt} FailUntilAttempt={FailUntilAttempt}",
            args.ScenarioName,
            context.RunId,
            context.Attempt,
            args.FailUntilAttempt);

        if (context.Attempt <= args.FailUntilAttempt)
        {
            throw new InvalidOperationException(
                $"[PostgresTestBed] Simulated failure for scenario '{args.ScenarioName}' at attempt {context.Attempt}.");
        }

        _logger.LogInformation(
            "[PostgresTestBed] Flaky failure demo succeeded. Scenario={ScenarioName} RunId={RunId} Attempt={Attempt}",
            args.ScenarioName,
            context.RunId,
            context.Attempt);

        return Task.CompletedTask;
    }
}

public sealed class FlakyFailureDemoArgs
{
    public string ScenarioName { get; set; } = "custom";

    public int FailUntilAttempt { get; set; }
}
