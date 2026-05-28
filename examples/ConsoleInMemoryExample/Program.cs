using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Hosting.Hosting;
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

// Required: register DurableStack services; this non-hosted sample uses in-memory storage.
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
var processor = provider.GetRequiredService<IDurableStackProcessor>();
var options = provider.GetRequiredService<DurableStackOptions>();

// Required for non-hosted apps: run migrations and recurring job initialization once.
await provider.InitializeDurableStackAsync(CancellationToken.None);

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
