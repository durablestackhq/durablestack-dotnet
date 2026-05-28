using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Hosting.DependencyInjection;
using Microsoft.Extensions.Logging;

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "console-heartbeat-every-minute")]
// Optional: make this job recurring; without this attribute it is enqueue-only.
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
