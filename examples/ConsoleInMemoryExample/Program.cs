using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Core.Scheduling;
using DurableStack.Hosting.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
var workerName = $"console-{Environment.MachineName}-{Environment.ProcessId}";

services.AddLogging(logging =>
{
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    logging.SetMinimumLevel(LogLevel.Information);
});

services.AddDurableStack(options =>
{
    options.StorageProvider = DurableStackStorageProvider.InMemory;
    options.WorkerName = workerName;
    options.PollInterval = TimeSpan.FromSeconds(1);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(5);
});

using var provider = services.BuildServiceProvider();

var startupLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var migrator = provider.GetRequiredService<IDurableStackStoreMigrator>();
var recurringInitializer = provider.GetRequiredService<IRecurringJobInitializer>();
var processor = provider.GetRequiredService<IDurableStackProcessor>();
var options = provider.GetRequiredService<DurableStackOptions>();

await migrator.MigrateAsync(CancellationToken.None);
await recurringInitializer.InitializeAsync(CancellationToken.None);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};

startupLogger.LogInformation(
    "Console in-memory example started. Press Ctrl+C to stop. WorkerName={WorkerName}",
    workerName);

while (!cts.IsCancellationRequested)
{
    try
    {
        await processor.ProcessOnceAsync(cts.Token);
        await Task.Delay(options.PollInterval, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        break;
    }
}

startupLogger.LogInformation("Console in-memory example stopped.");

[DurableJob(Name = "console-heartbeat-every-minute")]
[RecurringJob("* * * * *", TimeZone = "UTC")]
public sealed class ConsoleHeartbeatJob : IDurableJob
{
    private readonly ILogger<ConsoleHeartbeatJob> _logger;

    public ConsoleHeartbeatJob(ILogger<ConsoleHeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[ConsoleInMemoryExample] Recurring heartbeat executed. RunId={RunId}",
            context.RunId);
        return Task.CompletedTask;
    }
}
