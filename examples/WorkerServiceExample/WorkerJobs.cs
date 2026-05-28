using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Hosting.DependencyInjection;

namespace WorkerServiceExample;

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "cleanup-job")]
public sealed class CleanupJob : IDurableJob
{
    private readonly ILogger<CleanupJob> _logger;

    public CleanupJob(ILogger<CleanupJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing cleanup job. RunId={RunId} Attempt={Attempt}",
            context.RunId,
            context.Attempt);

        return Task.CompletedTask;
    }
}

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "worker-heartbeat-every-minute")]
// Optional: make this job recurring; without this attribute it is enqueue-only.
[RecurringJob("* * * * *", TimeZone = "UTC")]
public sealed class RecurringWorkerHeartbeatJob : IDurableJob
{
    private readonly ILogger<RecurringWorkerHeartbeatJob> _logger;

    public RecurringWorkerHeartbeatJob(ILogger<RecurringWorkerHeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing recurring worker heartbeat. RunId={RunId} Attempt={Attempt}",
            context.RunId,
            context.Attempt);

        return Task.CompletedTask;
    }
}

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "worker-long-running-lease-demo")]
public sealed class LongRunningLeaseDemoJob : IDurableJob
{
    private readonly ILogger<LongRunningLeaseDemoJob> _logger;

    public LongRunningLeaseDemoJob(ILogger<LongRunningLeaseDemoJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Long-running lease demo started. RunId={RunId}", context.RunId);
        await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        _logger.LogInformation("Long-running lease demo completed. RunId={RunId}", context.RunId);
    }
}
