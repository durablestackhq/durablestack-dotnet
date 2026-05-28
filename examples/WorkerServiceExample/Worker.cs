using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Hosting.DependencyInjection;

namespace WorkerServiceExample;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDurableStackClient _jobs;

    public Worker(ILogger<Worker> logger, IDurableStackClient jobs)
    {
        _logger = logger;
        _jobs = jobs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _jobs.EnqueueAsync<CleanupJob>(cancellationToken: stoppingToken);
        _logger.LogInformation("Queued CleanupJob at startup.");

        await _jobs.EnqueueAsync<LongRunningLeaseDemoJob>(cancellationToken: stoppingToken);
        _logger.LogInformation("Queued LongRunningLeaseDemoJob at startup.");
    }
}

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

[DurableJob(Name = "worker-heartbeat-every-minute")]
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
