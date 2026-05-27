using DurableStack.Hosting.DependencyInjection;
using DurableStack.Worker.Hosting;
using WorkerServiceExample;

var builder = Host.CreateApplicationBuilder(args);
var workerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";

builder.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(5);
});
// Uncomment to surface DurableStack lifecycle events (including worker heartbeats) in logs.
// builder.Services.UseDurableStackLoggingEventSink();
builder.Services.AddDurableStackOpenTelemetry();

builder.Services.AddLogging(logging => logging.AddSimpleConsole());

builder.Services.AddDurableJob<CleanupJob>("cleanup-job", job =>
{
    job.WithMaxAttempts(3);
});
builder.Services.AddDurableJob<RecurringWorkerHeartbeatJob>("worker-heartbeat-every-minute", job =>
{
    job.RunOnCron("* * * * *", timeZone: "UTC");
    job.WithMaxAttempts(3);
});
builder.Services.AddDurableJob<LongRunningLeaseDemoJob>("worker-long-running-lease-demo", job =>
{
    job.WithMaxAttempts(3);
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup")
    .LogInformation("DurableStack worker example started. Provider=Postgres WorkerName={WorkerName}", workerName);
host.Run();
