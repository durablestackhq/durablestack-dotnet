using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Sqlite.Storage;
using Microsoft.Data.Sqlite;

namespace DurableStack.Tests;

public sealed class SqliteIntegrationTests
{
    [Fact]
    public async Task ClaimDueRunsAsync_claims_run_once_across_multiple_workers()
    {
        await using var fixture = await SqliteFixture.CreateAsync("it_a_");
        var store = fixture.Store;

        await store.EnqueueAsync("job-a", "job-type-a", "{}", DateTimeOffset.UtcNow.AddSeconds(-1), 3, CancellationToken.None);

        var claims = await Task.WhenAll(
            store.ClaimDueRunsAsync("worker-1", 10, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-2", 10, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-3", 10, TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.Equal(1, claims.Sum(x => x.Count));
    }

    [Fact]
    public async Task MarkFailedAsync_with_retry_sets_pending_and_reschedules()
    {
        await using var fixture = await SqliteFixture.CreateAsync("it_b_");
        var store = fixture.Store;

        var runId = await store.EnqueueAsync("job-b", "job-type-b", null, DateTimeOffset.UtcNow.AddMinutes(-1), 3, CancellationToken.None);
        var claimed = await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(claimed);

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await store.MarkFailedAsync(runId, new InvalidOperationException("boom"), retry: true, retryAtUtc: retryAt, cancellationToken: CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("pending", run!.Status);
        Assert.True(run.ScheduledForUtc > DateTimeOffset.UtcNow.AddMinutes(4));
    }

    [Fact]
    public async Task TablePrefix_is_applied_and_preserves_case_for_sqlite()
    {
        await using var fixture = await SqliteFixture.CreateAsync("Acme_");

        var exists = await fixture.TableExistsAsync("Acme_durable_stack_job_runs");
        Assert.True(exists);
    }

    [Fact]
    public async Task EnsureMigrationsAppliedAsync_creates_tables_when_missing()
    {
        const string prefix = "it_mig_";
        await using var fixture = await SqliteFixture.CreateAsync(prefix);

        Assert.True(await fixture.TableExistsAsync($"{prefix}durable_stack_jobs"));
        Assert.True(await fixture.TableExistsAsync($"{prefix}durable_stack_job_runs"));
        Assert.True(await fixture.TableExistsAsync($"{prefix}durable_stack_job_locks"));
    }

    [Fact]
    public async Task ClaimDueRunsAsync_reclaims_expired_lease()
    {
        await using var fixture = await SqliteFixture.CreateAsync("it_lease_1_");
        var store = fixture.Store;

        await store.EnqueueAsync("job-lease", "job-type-lease", null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);

        var firstClaim = await store.ClaimDueRunsAsync("worker-initial", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Single(firstClaim);

        await Task.Delay(20);

        var secondClaim = await store.ClaimDueRunsAsync("worker-reclaimer", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(secondClaim);
        Assert.Equal("worker-reclaimer", secondClaim[0].LeaseOwner);
        Assert.Equal(2, secondClaim[0].Attempt);
    }

    [Fact]
    public async Task Parallel_workers_execute_single_due_run_once_effectively()
    {
        await using var fixture = await SqliteFixture.CreateAsync("it_pw_1_");
        var store = fixture.Store;

        await store.EnqueueAsync("job-parallel", "job-type-parallel", null, DateTimeOffset.UtcNow.AddSeconds(-1), 3, CancellationToken.None);

        var claims = await Task.WhenAll(
            store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-c", 1, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-d", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        Assert.Equal(1, claims.Sum(x => x.Count));

        var runs = await store.GetRunsAsync(CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("leased", run.Status);
        Assert.Equal(1, run.Attempt);
    }

    [Fact]
    public async Task TryMaterializeRecurringRunAsync_materializes_slot_once_across_concurrent_workers()
    {
        await using var fixture = await SqliteFixture.CreateAsync("it_rec_2_");
        var store = fixture.Store;

        var registration = new DurableJobRegistration
        {
            JobName = "every-minute-job",
            JobType = typeof(object),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        var dueAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await store.UpsertRecurringJobAsync(registration, dueAt, CancellationToken.None);

        var next = dueAt.AddMinutes(1);
        var recurring = new RecurringJobState
        {
            JobName = registration.JobName,
            CronExpression = registration.CronExpression!,
            TimeZone = registration.TimeZone,
            NextRunAtUtc = dueAt,
        };

        var results = await Task.WhenAll(
            store.TryMaterializeRecurringRunAsync(recurring, registration, next, CancellationToken.None),
            store.TryMaterializeRecurringRunAsync(recurring, registration, next, CancellationToken.None));

        Assert.Equal(1, results.Count(x => x));

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs, x => x.JobName == registration.JobName);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(string dbPath, SqliteJobStore store)
        {
            DbPath = dbPath;
            Store = store;
        }

        public string DbPath { get; }

        public SqliteJobStore Store { get; }

        public static async Task<SqliteFixture> CreateAsync(string prefix)
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"durablestack_{Guid.NewGuid():N}.db");
            var options = new DurableStackOptions
            {
                DatabaseTablePrefix = prefix,
                Sqlite = { ConnectionString = $"Data Source={dbPath}" },
                StorageProvider = DurableStackStorageProvider.Sqlite,
            };

            var store = new SqliteJobStore(options);
            await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
            return new SqliteFixture(dbPath, store);
        }

        public async Task<bool> TableExistsAsync(string tableName)
        {
            const string sql = """
                select exists (
                    select 1
                    from sqlite_master
                    where type = 'table'
                      and name = @table_name
                );
                """;

            await using var connection = new SqliteConnection($"Data Source={DbPath}");
            await connection.OpenAsync();
            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@table_name", tableName);
            var result = await command.ExecuteScalarAsync();
            return result is 1L;
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(DbPath))
            {
                try
                {
                    File.Delete(DbPath);
                }
                catch (IOException)
                {
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
