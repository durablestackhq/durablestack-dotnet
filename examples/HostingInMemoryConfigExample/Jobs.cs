using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Hosting.DependencyInjection;

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "send-welcome-email")]
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
            "Sending welcome email to {Email}. RunId={RunId} Attempt={Attempt}",
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

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "heartbeat-every-minute")]
// Optional: make this job recurring; without this attribute it is enqueue-only.
[RecurringJob("* * * * *", TimeZone = "UTC")]
public sealed class HeartbeatJob : IDurableJob
{
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recurring heartbeat executed. RunId={RunId}", context.RunId);
        return Task.CompletedTask;
    }
}

// Optional: pin a stable job name instead of defaulting to the class name.
[DurableJob(Name = "long-running-lease-demo")]
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
