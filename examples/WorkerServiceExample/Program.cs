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
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup")
    .LogInformation("DurableStack worker example started. Provider=Postgres WorkerName={WorkerName}", workerName);
host.Run();
