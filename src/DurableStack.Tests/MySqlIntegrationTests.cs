using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.MySql.Storage;
using MySqlConnector;

namespace DurableStack.Tests;

public sealed class MySqlIntegrationTests
{
    [Fact]
    public async Task ClaimDueRunsAsync_claims_run_once_across_multiple_workers()
    {
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "it_a_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_a_durable_stack_job_runs");

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
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "it_b_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_b_durable_stack_job_runs");

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
    public async Task TablePrefix_is_applied_and_preserves_case_for_mysql()
    {
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "Acme_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        var tableName = await GetActualTableNameAsync(connectionString, "Acme_durable_stack_job_runs");
        Assert.Equal("Acme_durable_stack_job_runs", tableName);
    }

    [Fact]
    public async Task EnsureMigrationsAppliedAsync_creates_tables_when_missing()
    {
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var prefix = $"it_mig_{Guid.NewGuid():N}_";
        var options = CreateOptions(connectionString, prefix);
        var store = new MySqlJobStore(options);

        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        Assert.True(await TableExistsAsync(connectionString, $"{prefix}durable_stack_jobs"));
        Assert.True(await TableExistsAsync(connectionString, $"{prefix}durable_stack_job_runs"));
        Assert.True(await TableExistsAsync(connectionString, $"{prefix}durable_stack_job_locks"));
    }

    [Fact]
    public async Task ClaimDueRunsAsync_reclaims_expired_lease()
    {
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "it_lease_1_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_lease_1_durable_stack_job_runs");

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
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "it_pw_1_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_pw_1_durable_stack_job_runs");

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
        var connectionString = GetConnectionStringOrSkip();
        if (connectionString is null)
        {
            return;
        }

        var options = CreateOptions(connectionString, prefix: "it_rec_2_");
        var store = new MySqlJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_rec_2_durable_stack_job_runs");
        await ClearJobsAsync(connectionString, "it_rec_2_durable_stack_jobs");

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

    private static DurableStackOptions CreateOptions(string connectionString, string prefix)
    {
        return new DurableStackOptions
        {
            DatabaseTablePrefix = prefix,
            MySql = { ConnectionString = connectionString },
            StorageProvider = DurableStackStorageProvider.MySql,
        };
    }

    private static string? GetConnectionStringOrSkip()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DURABLESTACK_TEST_MYSQL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        return null;
    }

    private static async Task<string?> GetActualTableNameAsync(string connectionString, string tableName)
    {
        const string sql = """
            select table_name
            from information_schema.tables
            where table_schema = database()
              and table_name = @table_name;
            """;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table_name", tableName);
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = database()
                  and table_name = @table_name
            );
            """;

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table_name", tableName);
        var result = await command.ExecuteScalarAsync();
        return result is true or 1L or 1UL;
    }

    private static async Task ClearRunsAsync(string connectionString, string tableName)
    {
        var sql = $"delete from `{tableName}`;";
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearJobsAsync(string connectionString, string tableName)
    {
        var sql = $"delete from `{tableName}`;";
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
