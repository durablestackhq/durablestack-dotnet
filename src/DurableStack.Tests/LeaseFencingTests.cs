using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Options;
using DurableStack.MySql.Storage;
using DurableStack.Postgres.Storage;
using DurableStack.Sqlite.Storage;
using DurableStack.SqlServer.Storage;
using Microsoft.Data.Sqlite;

namespace DurableStack.Tests;

/// <summary>
/// Store-contract guarantees around lease ownership, verified against every provider:
/// completion writes are fenced to the current lease owner, lease extension reports
/// loss, cancellation cannot be undone by a completing worker, and runs whose lease
/// expired with no attempts remaining are quarantined instead of reclaimed forever.
/// </summary>
public abstract class LeaseFencingContractTests
{
    /// <summary>Shortest lease the provider can express and expire.</summary>
    protected virtual TimeSpan ShortLease => TimeSpan.FromMilliseconds(1);

    /// <summary>How long to wait for <see cref="ShortLease"/> to lapse.</summary>
    protected virtual TimeSpan LeaseExpiryDelay => TimeSpan.FromMilliseconds(100);

    /// <summary>Creates a migrated, empty store; skips the test when the provider is unavailable.</summary>
    protected abstract Task<IDurableJobStore> CreateStoreAsync();

    [SkippableFact]
    public async Task Zombie_worker_cannot_overwrite_reclaimed_run()
    {
        var store = await CreateStoreAsync();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        var claimedByA = await store.ClaimDueRunsAsync("worker-a", 1, ShortLease, CancellationToken.None);
        Assert.Single(claimedByA);

        await Task.Delay(LeaseExpiryDelay);

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

    [SkippableFact]
    public async Task ExtendLeaseAsync_reports_lease_loss_after_reclaim()
    {
        var store = await CreateStoreAsync();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        Assert.Single(await store.ClaimDueRunsAsync("worker-a", 1, ShortLease, CancellationToken.None));
        await Task.Delay(LeaseExpiryDelay);
        Assert.Single(await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.False(await store.ExtendLeaseAsync(runId, "worker-a", TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.True(await store.ExtendLeaseAsync(runId, "worker-b", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [SkippableFact]
    public async Task Cancelled_run_is_not_resurrected_by_completing_worker()
    {
        var store = await CreateStoreAsync();
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

    [SkippableFact]
    public async Task Poison_run_is_quarantined_after_max_attempts_of_lease_expiry()
    {
        var store = await CreateStoreAsync();
        var runId = await store.EnqueueAsync("job-a", "job-type-a", null, DateTimeOffset.UtcNow.AddSeconds(-5), 2, CancellationToken.None);

        // Two claims that both "crash" (lease expires without a completion write).
        Assert.Single(await store.ClaimDueRunsAsync("worker-a", 1, ShortLease, CancellationToken.None));
        await Task.Delay(LeaseExpiryDelay);
        Assert.Single(await store.ClaimDueRunsAsync("worker-b", 1, ShortLease, CancellationToken.None));
        await Task.Delay(LeaseExpiryDelay);

        // Attempts are exhausted: the run must be quarantined, not claimed a third time.
        Assert.Empty(await store.ClaimDueRunsAsync("worker-c", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("failed", run!.Status);
        Assert.Equal(2, run.Attempt);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Contains("no attempts remaining", run.ErrorMessage);
    }

    protected static string UniquePrefix(string tag)
    {
        // Must satisfy the 16-character DatabaseTablePrefix limit.
        return $"{tag}{Guid.NewGuid().ToString("N")[..8]}_";
    }
}

public sealed class InMemoryLeaseFencingTests : LeaseFencingContractTests
{
    protected override Task<IDurableJobStore> CreateStoreAsync()
    {
        return Task.FromResult<IDurableJobStore>(new InMemoryJobStore());
    }
}

public sealed class SqliteLeaseFencingTests : LeaseFencingContractTests, IAsyncLifetime
{
    private string? _dbPath;

    protected override async Task<IDurableJobStore> CreateStoreAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"durablestack_lf_{Guid.NewGuid():N}.db");
        var options = new DurableStackOptions
        {
            Sqlite = { ConnectionString = $"Data Source={_dbPath}" },
        };
        var store = new SqliteJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        return store;
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_dbPath is not null)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class PostgresLeaseFencingTests : LeaseFencingContractTests
{
    protected override async Task<IDurableJobStore> CreateStoreAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("DURABLESTACK_TEST_POSTGRES");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "DURABLESTACK_TEST_POSTGRES is not set; set it to a PostgreSQL connection string to run this test.");

        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = UniquePrefix("lfp_"),
            Postgres = { ConnectionString = connectionString!.Trim() },
        };
        var store = new PostgresJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        return store;
    }
}

public sealed class MySqlLeaseFencingTests : LeaseFencingContractTests
{
    protected override async Task<IDurableJobStore> CreateStoreAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("DURABLESTACK_TEST_MYSQL");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "DURABLESTACK_TEST_MYSQL is not set; set it to a MySQL connection string to run this test.");

        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = UniquePrefix("lfm_"),
            MySql = { ConnectionString = connectionString!.Trim() },
        };
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        return store;
    }
}

public sealed class SqlServerLeaseFencingTests : LeaseFencingContractTests
{
    // The SQL Server store rounds leases up to whole seconds (DATEADD(second, ...)).
    protected override TimeSpan ShortLease => TimeSpan.FromSeconds(1);

    protected override TimeSpan LeaseExpiryDelay => TimeSpan.FromMilliseconds(1500);

    protected override async Task<IDurableJobStore> CreateStoreAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("DURABLESTACK_TEST_SQLSERVER");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "DURABLESTACK_TEST_SQLSERVER is not set; set it to a SQL Server connection string to run this test.");

        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = UniquePrefix("lfs_"),
            SqlServer = { ConnectionString = connectionString!.Trim() },
        };
        var store = new SqlServerJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        return store;
    }
}
