using DurableStack.Core.Execution;
using DurableStack.Core.Models;

namespace DurableStack.Tests;

public sealed class InMemoryRetentionTests
{
    [Fact]
    public async Task PruneHistoricalRunsAsync_removes_terminal_runs_in_batches()
    {
        var store = new InMemoryJobStore();

        var succeededId = await store.EnqueueAsync(
            "job-a",
            "job-a-type",
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts: 3,
            CancellationToken.None);

        var failedId = await store.EnqueueAsync(
            "job-b",
            "job-b-type",
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts: 3,
            CancellationToken.None);

        _ = await store.EnqueueAsync(
            "job-c",
            "job-c-type",
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts: 3,
            CancellationToken.None);

        await store.MarkSucceededAsync(succeededId, CancellationToken.None);
        await store.MarkFailedAsync(failedId, new InvalidOperationException("boom"), retry: false, retryAtUtc: null, CancellationToken.None);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(1);

        var firstDeleted = await store.PruneHistoricalRunsAsync(cutoff, batchSize: 1, CancellationToken.None);
        Assert.Equal(1, firstDeleted);

        var secondDeleted = await store.PruneHistoricalRunsAsync(cutoff, batchSize: 10, CancellationToken.None);
        Assert.Equal(1, secondDeleted);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs);
        Assert.Equal("pending", runs[0].Status);
    }

    [Fact]
    public async Task PruneHistoricalRunsAsync_does_not_remove_recurring_schedule_definitions()
    {
        var store = new InMemoryJobStore();
        var registration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(object),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(registration, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        var runId = await store.EnqueueAsync(
            "heartbeat",
            "heartbeat-type",
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts: 3,
            CancellationToken.None);

        await store.MarkSucceededAsync(runId, CancellationToken.None);
        _ = await store.PruneHistoricalRunsAsync(DateTimeOffset.UtcNow.AddMinutes(1), batchSize: 100, CancellationToken.None);

        var schedules = await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None);
        Assert.Single(schedules);
        Assert.Equal("heartbeat", schedules[0].JobName);
    }
}
