using DurableStack.Core.Abstractions;

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
        // Optional: enqueue startup work to verify the pipeline end-to-end.
        await _jobs.EnqueueAsync<CleanupJob>(cancellationToken: stoppingToken);
        _logger.LogInformation("Queued CleanupJob at startup.");

        // Optional: enqueue a long-running job to observe lease renewal behavior.
        await _jobs.EnqueueAsync<LongRunningLeaseDemoJob>(cancellationToken: stoppingToken);
        _logger.LogInformation("Queued LongRunningLeaseDemoJob at startup.");
    }
}
