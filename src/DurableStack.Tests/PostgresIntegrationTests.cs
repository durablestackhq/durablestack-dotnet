using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Postgres.Storage;
using Npgsql;

namespace DurableStack.Tests;

public sealed class PostgresIntegrationTests
{
    [SkippableFact]
    public async Task ClaimDueRunsAsync_claims_run_once_across_multiple_workers()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "it_a_");
        var store = new PostgresJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_a_durable_stack_job_runs");

        await store.EnqueueAsync("job-a", "job-type-a", "{}", DateTimeOffset.UtcNow.AddSeconds(-1), 3, CancellationToken.None);

        var claims = await Task.WhenAll(
            store.ClaimDueRunsAsync("worker-1", 10, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-2", 10, TimeSpan.FromSeconds(30), CancellationToken.None),
            store.ClaimDueRunsAsync("worker-3", 10, TimeSpan.FromSeconds(30), CancellationToken.None));

        var totalClaimed = claims.Sum(x => x.Count);
        Assert.Equal(1, totalClaimed);
    }

    [SkippableFact]
    public async Task MarkFailedAsync_with_retry_sets_pending_and_reschedules()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "it_b_");
        var store = new PostgresJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await ClearRunsAsync(connectionString, "it_b_durable_stack_job_runs");

        var runId = await store.EnqueueAsync("job-b", "job-type-b", null, DateTimeOffset.UtcNow.AddMinutes(-1), 3, CancellationToken.None);
        var claimed = await store.ClaimDueRunsAsync("worker-b", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(claimed);

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await store.MarkFailedAsync(runId, "worker-b", new InvalidOperationException("boom"), retry: true, retryAtUtc: retryAt, cancellationToken: CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("pending", run!.Status);
        Assert.True(run.ScheduledForUtc > DateTimeOffset.UtcNow.AddMinutes(4));
    }

    [SkippableFact]
    public async Task TablePrefix_is_applied_and_lowercased_for_postgres()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "Acme_");
        var store = new PostgresJobStore(options);
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        var exists = await TableExistsAsync(connectionString, "acme_durable_stack_job_runs");
        Assert.True(exists);
    }

    [SkippableFact]
    public async Task EnsureMigrationsAppliedAsync_creates_tables_when_missing()
    {
        var connectionString = GetConnectionStringOrSkip();

        var prefix = $"mig_c_{Guid.NewGuid().ToString("N")[..8]}_";
        var options = CreateOptions(connectionString, prefix);
        var store = new PostgresJobStore(options);

        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        Assert.True(await TableExistsAsync(connectionString, $"{prefix.ToLowerInvariant()}durable_stack_jobs"));
        Assert.True(await TableExistsAsync(connectionString, $"{prefix.ToLowerInvariant()}durable_stack_job_runs"));
        Assert.True(await TableExistsAsync(connectionString, $"{prefix.ToLowerInvariant()}durable_stack_schema_migrations"));
        Assert.False(await TableExistsAsync(connectionString, $"{prefix.ToLowerInvariant()}durable_stack_job_locks"));
    }

    [SkippableFact]
    public async Task EnsureMigrationsAppliedAsync_is_idempotent_and_preserves_existing_runs()
    {
        var connectionString = GetConnectionStringOrSkip();

        var prefix = $"mig_i_{Guid.NewGuid().ToString("N")[..8]}_";
        var options = CreateOptions(connectionString, prefix);
        var store = new PostgresJobStore(options);

        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        await store.EnqueueAsync("job-mig", "job-type-mig", null, DateTimeOffset.UtcNow.AddSeconds(-1), 3, CancellationToken.None);

        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs);

        var claims = await store.ClaimDueRunsAsync("worker-mig", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(claims);
    }

    [SkippableFact]
    public async Task EnsureMigrationsAppliedAsync_is_safe_under_concurrent_startup()
    {
        var connectionString = GetConnectionStringOrSkip();

        var prefix = $"migc_{Guid.NewGuid().ToString("N")[..8]}_";
        var options = CreateOptions(connectionString, prefix);

        var stores = Enumerable.Range(0, 5).Select(_ => new PostgresJobStore(options)).ToArray();
        await Task.WhenAll(stores.Select(s => s.EnsureMigrationsAppliedAsync(CancellationToken.None)));

        // The schema is usable and each migration step was recorded exactly once.
        await stores[0].EnqueueAsync("job-mig-c", "job-type-mig-c", null, DateTimeOffset.UtcNow.AddSeconds(-1), 3, CancellationToken.None);
        Assert.Single(await stores[0].ClaimDueRunsAsync("worker-mig-c", 1, TimeSpan.FromSeconds(30), CancellationToken.None));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {prefix}durable_stack_schema_migrations", connection);
        var versions = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(2, versions);
    }

    [SkippableFact]
    public async Task EnsureMigrationsAppliedAsync_upgrades_legacy_schema_and_drops_locks_table()
    {
        var connectionString = GetConnectionStringOrSkip();

        var prefix = $"migu_{Guid.NewGuid().ToString("N")[..8]}_";
        var options = CreateOptions(connectionString, prefix);
        var store = new PostgresJobStore(options);

        // Simulate a v1.0.1 deployment: schema present, legacy locks table present,
        // and no recorded schema version.
        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);
        var runId = await store.EnqueueAsync("job-upgrade", "job-type-upgrade", null, DateTimeOffset.UtcNow.AddMinutes(5), 3, CancellationToken.None);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var legacy = new NpgsqlCommand($"create table {prefix}durable_stack_job_locks (lock_key text primary key)", connection))
            {
                await legacy.ExecuteNonQueryAsync();
            }

            await using (var wipe = new NpgsqlCommand($"delete from {prefix}durable_stack_schema_migrations", connection))
            {
                await wipe.ExecuteNonQueryAsync();
            }
        }

        await store.EnsureMigrationsAppliedAsync(CancellationToken.None);

        Assert.False(await TableExistsAsync(connectionString, $"{prefix}durable_stack_job_locks"));
        Assert.NotNull(await store.GetRunAsync(runId, CancellationToken.None));
    }

    [SkippableFact]
    public async Task ClaimDueRunsAsync_reclaims_expired_lease()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "it_lease_1_");
        var store = new PostgresJobStore(options);
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

    [SkippableFact]
    public async Task Parallel_workers_execute_single_due_run_once_effectively()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "it_pw_1_");
        var store = new PostgresJobStore(options);
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

    [SkippableFact]
    public async Task TryMaterializeRecurringRunAsync_materializes_slot_once_across_concurrent_workers()
    {
        var connectionString = GetConnectionStringOrSkip();

        var options = CreateOptions(connectionString, prefix: "it_rec_2_");
        var store = new PostgresJobStore(options);
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
            JobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
            CronExpression = registration.CronExpression!,
            TimeZone = registration.TimeZone,
            MaxAttempts = registration.MaxAttempts,
            Enabled = true,
            AllowConcurrentRuns = registration.AllowConcurrentRuns,
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
            Postgres = { ConnectionString = connectionString },
            StorageProvider = DurableStackStorageProvider.Postgres,
        };
    }

    private static string GetConnectionStringOrSkip()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DURABLESTACK_TEST_POSTGRES");
        Skip.If(string.IsNullOrWhiteSpace(fromEnv), "DURABLESTACK_TEST_POSTGRES is not set; set it to a PostgreSQL connection string to run this test.");
        return fromEnv!.Trim();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'public'
                  and table_name = @table_name
            );
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task ClearRunsAsync(string connectionString, string tableName)
    {
        var sql = $"delete from {tableName};";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearJobsAsync(string connectionString, string tableName)
    {
        var sql = $"delete from {tableName};";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
