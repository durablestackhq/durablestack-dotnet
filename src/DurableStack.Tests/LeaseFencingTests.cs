using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Execution;

namespace DurableStack.Tests;

/// <summary>
/// Store-level guarantees around lease ownership: completion writes are fenced to the
/// current lease owner, lease extension reports loss, and runs whose lease expired with
/// no attempts remaining are quarantined instead of reclaimed forever.
/// </summary>
public sealed class LeaseFencingTests
{
    [Fact]
    public async Task Zombie_worker_cannot_overwrite_reclaimed_run()
    {
        var store = new InMemoryJobStore();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        var claimedByA = await store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Single(claimedByA);

        await Task.Delay(20);

        var claimedByB = await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(claimedByB);
        Assert.Equal(2, claimedByB[0].Attempt);

        // Worker A wakes up and tries to record its stale outcome: both writes must be rejected.
        var failedRecorded = await store.MarkFailedAsync(runId, "worker-a", new InvalidOperationException("boom"), retry: true, retryAtUtc: DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.False(failedRecorded);
        var succeededRecorded = await store.MarkSucceededAsync(runId, "worker-a", CancellationToken.None);
        Assert.False(succeededRecorded);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("leased", run!.Status);
        Assert.Equal("worker-b", run.LeaseOwner);

        // The actual owner's write goes through.
        Assert.True(await store.MarkSucceededAsync(runId, "worker-b", CancellationToken.None));
        run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.Equal("succeeded", run!.Status);
    }

    [Fact]
    public async Task ExtendLeaseAsync_reports_lease_loss_after_reclaim()
    {
        var store = new InMemoryJobStore();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        Assert.Single(await store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None));
        await Task.Delay(20);
        Assert.Single(await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.False(await store.ExtendLeaseAsync(runId, "worker-a", TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.True(await store.ExtendLeaseAsync(runId, "worker-b", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_run_is_not_resurrected_by_completing_worker()
    {
        var store = new InMemoryJobStore();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        Assert.Single(await store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.True(await store.CancelRunAsync(runId, CancellationToken.None));

        // The worker finishes the job anyway; its outcome must not undo the cancellation.
        Assert.False(await store.MarkSucceededAsync(runId, "worker-a", CancellationToken.None));

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("failed", run!.Status);
        Assert.Equal("Run was cancelled.", run.ErrorMessage);
    }

    [Fact]
    public async Task Poison_run_is_quarantined_after_max_attempts_of_lease_expiry()
    {
        var store = new InMemoryJobStore();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 2, CancellationToken.None);

        // Two claims that both "crash" (lease expires without a completion write).
        Assert.Single(await store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None));
        await Task.Delay(20);
        Assert.Single(await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None));
        await Task.Delay(20);

        // Attempts are exhausted: the run must be quarantined, not claimed a third time.
        Assert.Empty(await store.ClaimDueRunsAsync("worker-c", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("failed", run!.Status);
        Assert.Equal(2, run.Attempt);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Contains("no attempts remaining", run.ErrorMessage);
    }
}
