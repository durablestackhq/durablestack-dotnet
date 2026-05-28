using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core;
using DurableStack.Core.Abstractions;

namespace DurableStack.Tests;

public sealed class DiscoveryDefaultJob : IDurableJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[DurableJob(Name = "discovery-recurring-job", MaxAttempts = 5)]
[RecurringJob("*/5 * * * *", TimeZone = "UTC")]
public sealed class DiscoveryRecurringJob : IDurableJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class DiscoveryPayload
{
    public string Value { get; set; } = string.Empty;
}

[DurableJob(MaxAttempts = 4)]
public sealed class DiscoveryArgsJob : IDurableJob<DiscoveryPayload>
{
    public Task ExecuteAsync(DiscoveryPayload args, JobContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
