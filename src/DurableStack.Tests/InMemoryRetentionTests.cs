using DurableStack.Core.Execution;

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
}
