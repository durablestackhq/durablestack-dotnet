using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Data.Sqlite;

namespace DurableStack.Sqlite.Storage;

/// <summary>
/// SQLite-backed job store intended for single-node or dev/test scenarios. SQLite's
/// single-writer immediate transactions serialize claiming, so concurrent workers on
/// the same database cannot double-claim, and leases are evaluated against the
/// worker's clock (UTC). Timestamps are stored as ISO-8601 UTC text. Completion writes
/// are lease-fenced, and a run whose lease expired with no attempts remaining is
/// failed terminally at claim time rather than reclaimed.
/// </summary>
public sealed class SqliteJobStore : IDurableJobStore
{
    private const string ExhaustedLeaseErrorMessage =
        "Lease expired with no attempts remaining; the worker likely crashed during execution.";

    // v1: initial schema. v2: drop the never-used job_locks table (SQLite's
    // single-writer transaction is used instead).
    private const int CurrentSchemaVersion = 2;

    private readonly string _connectionString;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;
    private readonly string _migrationsTable;

    /// <summary>
    /// Initializes the store from the configured options. Requires a SQLite connection
    /// string; table names are resolved from the configured table prefix.
    /// </summary>
    public SqliteJobStore(DurableStackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString))
        {
            throw new ArgumentException("SQLite connection string is required.", nameof(options));
        }

        _connectionString = options.Sqlite.ConnectionString;
        _jobsTable = Quote(SqliteTableNameResolver.Jobs(options));
        _runsTable = Quote(SqliteTableNameResolver.Runs(options));
        _locksTable = Quote(SqliteTableNameResolver.Locks(options));
        _migrationsTable = Quote(SqliteTableNameResolver.Migrations(options));
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        var sql = $"""
            insert into {_runsTable} (
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                attempt,
                max_attempts,
                created_at_utc,
                updated_at_utc)
            values (
                @id,
                @job_name,
                @job_type,
                'pending',
                @payload_json,
                @scheduled_for_utc,
                null,
                0,
                @max_attempts,
                @now_utc,
                @now_utc);
            """;

        var nowUtc = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@id", runId.ToString()),
            new SqliteParameter("@job_name", jobName),
            new SqliteParameter("@job_type", jobType),
            new SqliteParameter("@payload_json", (object?)payloadJson ?? DBNull.Value),
            new SqliteParameter("@scheduled_for_utc", ToDbTimestamp(scheduledForUtc)),
            new SqliteParameter("@max_attempts", maxAttempts),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));

        return runId;
    }

    /// <inheritdoc />
    public async Task<Guid?> TryEnqueueIfNoActiveRunAsync(
        string jobName,
        string jobType,
        string? payloadJson,
        DateTimeOffset scheduledForUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        var sql = $"""
            insert into {_runsTable} (
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                attempt,
                max_attempts,
                created_at_utc,
                updated_at_utc)
            select
                @id,
                @job_name,
                @job_type,
                'pending',
                @payload_json,
                @scheduled_for_utc,
                null,
                0,
                @max_attempts,
                @now_utc,
                @now_utc
            where not exists (
                select 1
                from {_runsTable}
                where job_name = @job_name
                  and status in ('pending', 'leased'));
            """;

        var nowUtc = DateTimeOffset.UtcNow;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());
        command.Parameters.AddWithValue("@job_name", jobName);
        command.Parameters.AddWithValue("@job_type", jobType);
        command.Parameters.AddWithValue("@payload_json", (object?)payloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@scheduled_for_utc", ToDbTimestamp(scheduledForUtc));
        command.Parameters.AddWithValue("@max_attempts", maxAttempts);
        command.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0 ? runId : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var leaseUntilUtc = nowUtc.Add(leaseDuration);
        var claimed = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // Quarantine poison runs: a run whose lease expired with no attempts left
        // (worker crashed mid-execution) is failed terminally instead of being
        // reclaimed and crash-looping forever.
        var reapSql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = @now_utc,
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                error_detail = null,
                updated_at_utc = @now_utc
            where status = 'leased'
              and lease_until_utc is not null
              and lease_until_utc <= @now_utc
              and attempt >= max_attempts;
            """;

        await using (var reap = new SqliteCommand(reapSql, connection, transaction))
        {
            reap.Parameters.AddWithValue("@error_message", ExhaustedLeaseErrorMessage);
            reap.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));
            await reap.ExecuteNonQueryAsync(cancellationToken);
        }

        var ids = new List<string>();
        var selectSql = $"""
            select id
            from {_runsTable}
            where attempt < max_attempts
              and ((
                status = 'pending'
                and scheduled_for_utc <= @now_utc)
               or (
                status = 'leased'
                and lease_until_utc is not null
                and lease_until_utc <= @now_utc))
            order by scheduled_for_utc asc
            limit @batch_size;
            """;

        await using (var select = new SqliteCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));
            select.Parameters.AddWithValue("@batch_size", batchSize);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetString(0));
            }
        }

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }

        var placeholders = string.Join(", ", ids.Select((_, i) => $"@id{i}"));

        var updateSql = $"""
            update {_runsTable}
            set
                status = 'leased',
                lease_owner = @worker_name,
                lease_until_utc = @lease_until_utc,
                started_at_utc = coalesce(started_at_utc, @now_utc),
                attempt = attempt + 1,
                updated_at_utc = @now_utc
            where id in ({placeholders})
              and (
                (status = 'pending' and scheduled_for_utc <= @now_utc)
                or (status = 'leased' and lease_until_utc is not null and lease_until_utc <= @now_utc));
            """;

        await using (var update = new SqliteCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("@worker_name", workerName);
            update.Parameters.AddWithValue("@lease_until_utc", ToDbTimestamp(leaseUntilUtc));
            update.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));

            for (var i = 0; i < ids.Count; i++)
            {
                update.Parameters.AddWithValue($"@id{i}", ids[i]);
            }

            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var fetchSql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            where id in ({placeholders})
              and lease_owner = @worker_name
              and lease_until_utc = @lease_until_utc;
            """;

        await using (var fetch = new SqliteCommand(fetchSql, connection, transaction))
        {
            fetch.Parameters.AddWithValue("@worker_name", workerName);
            fetch.Parameters.AddWithValue("@lease_until_utc", ToDbTimestamp(leaseUntilUtc));

            for (var i = 0; i < ids.Count; i++)
            {
                fetch.Parameters.AddWithValue($"@id{i}", ids[i]);
            }

            await using var reader = await fetch.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(MapRun(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    /// <inheritdoc />
    public async Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        // Fenced write: only the current lease owner may record the outcome, so a
        // worker whose lease was reclaimed cannot overwrite the new owner's state.
        var sql = $"""
            update {_runsTable}
            set
                status = 'succeeded',
                completed_at_utc = @now_utc,
                lease_owner = null,
                lease_until_utc = null,
                error_message = null,
                error_detail = null,
                updated_at_utc = @now_utc
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@id", runId.ToString()),
            new SqliteParameter("@worker_name", workerName),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        var sql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = @now_utc,
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                updated_at_utc = @now_utc
            where id = @id
              and status in ('pending', 'leased');
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());
        command.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));
        command.Parameters.AddWithValue("@error_message", "Run was cancelled.");

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<bool> MarkFailedAsync(
        Guid runId,
        string workerName,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        string sql;
        List<SqliteParameter> parameters;

        if (retry && retryAtUtc.HasValue)
        {
            sql = $"""
                update {_runsTable}
                set
                    status = 'pending',
                    scheduled_for_utc = @retry_at_utc,
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    completed_at_utc = null,
                    updated_at_utc = @now_utc
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;

            parameters =
            [
                new SqliteParameter("@id", runId.ToString()),
                new SqliteParameter("@worker_name", workerName),
                new SqliteParameter("@retry_at_utc", ToDbTimestamp(retryAtUtc.Value)),
                new SqliteParameter("@error_message", exception.Message),
                new SqliteParameter("@error_detail", exception.ToString()),
                new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)),
            ];
        }
        else
        {
            sql = $"""
                update {_runsTable}
                set
                    status = 'failed',
                    completed_at_utc = @now_utc,
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    updated_at_utc = @now_utc
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;

            parameters =
            [
                new SqliteParameter("@id", runId.ToString()),
                new SqliteParameter("@worker_name", workerName),
                new SqliteParameter("@error_message", exception.Message),
                new SqliteParameter("@error_detail", exception.ToString()),
                new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)),
            ];
        }

        var affected = await ExecuteNonQueryAsync(sql, cancellationToken, parameters.ToArray());
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            where id = @id;
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapRun(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            order by scheduled_for_utc desc;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            where job_name = @job_name
            order by scheduled_for_utc desc
            limit @take;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@job_name", jobName);
        command.Parameters.AddWithValue("@take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            order by scheduled_for_utc desc
            limit @take;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            where status = @status
            order by scheduled_for_utc desc
            limit @take;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                started_at_utc,
                completed_at_utc,
                attempt,
                max_attempts,
                lease_owner,
                lease_until_utc,
                error_message
            from {_runsTable}
            where schedule_slot_utc is null
            order by scheduled_for_utc desc
            limit @take;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@take", Math.Max(1, take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                name,
                job_type,
                cron_expression,
                time_zone,
                max_attempts,
                retry_behavior,
                retry_initial_delay_seconds,
                enabled,
                allow_concurrent_runs,
                next_run_at_utc
            from {_jobsTable}
            where schedule_type = 'cron'
              and cron_expression is not null
              and (@include_disabled = 1 or enabled = 1)
            order by name asc;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@include_disabled", includeDisabled ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new RecurringJobState
            {
                JobName = reader.GetString(reader.GetOrdinal("name")),
                JobType = reader.GetString(reader.GetOrdinal("job_type")),
                CronExpression = reader.GetString(reader.GetOrdinal("cron_expression")),
                TimeZone = reader.GetString(reader.GetOrdinal("time_zone")),
                MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
                RetryBehavior = ParseRetryBehavior(reader, "retry_behavior"),
                RetryInitialDelaySeconds = AsOptionalInt32(reader, "retry_initial_delay_seconds"),
                Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
                AllowConcurrentRuns = reader.GetInt32(reader.GetOrdinal("allow_concurrent_runs")) == 1,
                NextRunAtUtc = AsOptionalUtcDateTimeOffset(reader, "next_run_at_utc") ?? DateTimeOffset.MinValue,
            });
        }

        return jobs;
    }

    /// <inheritdoc />
    public async Task<bool> SetRecurringJobEnabledAsync(
        string jobName,
        bool enabled,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_jobsTable}
            set
                enabled = @enabled,
                next_run_at_utc = @next_run_at_utc,
                updated_at_utc = @now_utc
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("@next_run_at_utc", nextRunAtUtc.HasValue ? ToDbTimestamp(nextRunAtUtc.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@now_utc", ToDbTimestamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("@name", jobName);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRecurringJobScheduleAsync(
        string jobName,
        string cronExpression,
        string timeZone,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_jobsTable}
            set
                cron_expression = @cron_expression,
                time_zone = @time_zone,
                next_run_at_utc = @next_run_at_utc,
                updated_at_utc = @now_utc
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@cron_expression", cronExpression);
        command.Parameters.AddWithValue("@time_zone", timeZone);
        command.Parameters.AddWithValue("@next_run_at_utc", ToDbTimestamp(nextRunAtUtc));
        command.Parameters.AddWithValue("@now_utc", ToDbTimestamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("@name", jobName);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            delete from {_runsTable}
            where id in (
                select id
                from {_runsTable}
                where status in ('succeeded', 'failed')
                  and completed_at_utc is not null
                  and completed_at_utc < @completed_before_utc
                order by completed_at_utc asc
                limit @batch_size
            );
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@completed_before_utc", ToDbTimestamp(completedBeforeUtc));
        command.Parameters.AddWithValue("@batch_size", Math.Max(1, batchSize));

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertRecurringJobAsync(
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        if (!registration.IsRecurring || string.IsNullOrWhiteSpace(registration.CronExpression))
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;

        var sql = $"""
            insert into {_jobsTable} (
                id,
                name,
                job_type,
                schedule_type,
                cron_expression,
                time_zone,
                enabled,
                allow_concurrent_runs,
                retry_behavior,
                retry_initial_delay_seconds,
                max_attempts,
                next_run_at_utc,
                created_at_utc,
                updated_at_utc)
            values (
                @id,
                @name,
                @job_type,
                'cron',
                @cron_expression,
                @time_zone,
                @enabled,
                @allow_concurrent_runs,
                @retry_behavior,
                @retry_initial_delay_seconds,
                @max_attempts,
                @next_run_at_utc,
                @now_utc,
                @now_utc)
            on conflict(name) do update set
                job_type = excluded.job_type,
                schedule_type = 'cron',
                cron_expression = excluded.cron_expression,
                time_zone = excluded.time_zone,
                enabled = excluded.enabled,
                allow_concurrent_runs = excluded.allow_concurrent_runs,
                retry_behavior = excluded.retry_behavior,
                retry_initial_delay_seconds = excluded.retry_initial_delay_seconds,
                max_attempts = excluded.max_attempts,
                next_run_at_utc = excluded.next_run_at_utc,
                updated_at_utc = excluded.updated_at_utc;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@id", Guid.NewGuid().ToString()),
            new SqliteParameter("@name", registration.JobName),
            new SqliteParameter("@job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name),
            new SqliteParameter("@cron_expression", registration.CronExpression),
            new SqliteParameter("@time_zone", registration.TimeZone),
            new SqliteParameter("@enabled", registration.Enabled ? 1 : 0),
            new SqliteParameter("@allow_concurrent_runs", registration.AllowConcurrentRuns ? 1 : 0),
            new SqliteParameter("@retry_behavior", (object?)ToRetryBehaviorValue(registration.RetryBehavior) ?? DBNull.Value),
            new SqliteParameter("@retry_initial_delay_seconds", (object?)registration.RetryInitialDelaySeconds ?? DBNull.Value),
            new SqliteParameter("@max_attempts", registration.MaxAttempts),
            new SqliteParameter("@next_run_at_utc", ToDbTimestamp(nextRunAtUtc)),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                name,
                job_type,
                cron_expression,
                time_zone,
                max_attempts,
                retry_behavior,
                retry_initial_delay_seconds,
                enabled,
                allow_concurrent_runs,
                next_run_at_utc
            from {_jobsTable}
            where enabled = 1
              and schedule_type = 'cron'
              and next_run_at_utc is not null
              and next_run_at_utc <= @now_utc
            order by next_run_at_utc asc
            limit @batch_size;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));
        command.Parameters.AddWithValue("@batch_size", batchSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new RecurringJobState
            {
                JobName = reader.GetString(reader.GetOrdinal("name")),
                JobType = reader.GetString(reader.GetOrdinal("job_type")),
                CronExpression = reader.GetString(reader.GetOrdinal("cron_expression")),
                TimeZone = reader.GetString(reader.GetOrdinal("time_zone")),
                MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
                RetryBehavior = ParseRetryBehavior(reader, "retry_behavior"),
                RetryInitialDelaySeconds = AsOptionalInt32(reader, "retry_initial_delay_seconds"),
                Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
                AllowConcurrentRuns = reader.GetInt32(reader.GetOrdinal("allow_concurrent_runs")) == 1,
                NextRunAtUtc = AsUtcDateTimeOffset(reader, "next_run_at_utc"),
            });
        }

        return jobs;
    }

    /// <inheritdoc />
    public async Task UpdateRecurringNextRunAsync(
        string jobName,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                updated_at_utc = @now_utc
            where name = @name;
            """;

        var nowUtc = DateTimeOffset.UtcNow;
        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@next_run_at_utc", ToDbTimestamp(nextRunAtUtc)),
            new SqliteParameter("@name", jobName),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));
    }

    /// <inheritdoc />
    public async Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var scheduledFor = ToDbTimestamp(recurring.NextRunAtUtc);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var updateSql = $"""
            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                last_run_at_utc = @scheduled_for_utc,
                updated_at_utc = @now_utc
            where name = @name
              and enabled = 1
              and schedule_type = 'cron'
              and next_run_at_utc = @expected_next_run_at_utc
              and (
                (
                    allow_concurrent_runs = 0
                    and not exists (
                        select 1
                        from {_runsTable} active
                        where active.job_name = @name
                          and active.status in ('pending', 'leased'))
                )
                or (
                    allow_concurrent_runs = 1
                    and not exists (
                        select 1
                        from {_runsTable} active
                        where active.job_name = @name
                          and active.status = 'pending')
                ));
            """;

        await using (var update = new SqliteCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("@next_run_at_utc", ToDbTimestamp(nextRunAtUtc));
            update.Parameters.AddWithValue("@scheduled_for_utc", scheduledFor);
            update.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));
            update.Parameters.AddWithValue("@name", recurring.JobName);
            update.Parameters.AddWithValue("@expected_next_run_at_utc", scheduledFor);

            var updated = await update.ExecuteNonQueryAsync(cancellationToken);
            if (updated == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        var insertSql = $"""
            insert into {_runsTable} (
                id,
                job_name,
                job_type,
                status,
                payload_json,
                scheduled_for_utc,
                schedule_slot_utc,
                attempt,
                max_attempts,
                created_at_utc,
                updated_at_utc)
            values (
                @run_id,
                @job_name,
                @job_type,
                'pending',
                null,
                @scheduled_for_utc,
                @scheduled_for_utc,
                0,
                @max_attempts,
                @now_utc,
                @now_utc);
            """;

        await using (var insert = new SqliteCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("@run_id", runId.ToString());
            insert.Parameters.AddWithValue("@job_name", registration.JobName);
            insert.Parameters.AddWithValue("@job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name);
            insert.Parameters.AddWithValue("@scheduled_for_utc", scheduledFor);
            insert.Parameters.AddWithValue("@max_attempts", registration.MaxAttempts);
            insert.Parameters.AddWithValue("@now_utc", ToDbTimestamp(nowUtc));

            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var leaseUntilUtc = nowUtc.Add(leaseDuration);

        var sql = $"""
            update {_runsTable}
            set
                lease_until_utc = @lease_until_utc,
                updated_at_utc = @now_utc
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@lease_until_utc", ToDbTimestamp(leaseUntilUtc)),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)),
            new SqliteParameter("@id", runId.ToString()),
            new SqliteParameter("@worker_name", workerName));
        return affected > 0;
    }

    /// <summary>
    /// Creates or upgrades the schema to the current version, recording each applied
    /// version in the schema migrations table. SQLite's single-writer transaction
    /// serializes concurrent migrators, and its transactional DDL applies each migration
    /// atomically. Returns when the schema is already current.
    /// </summary>
    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var versionTable = new SqliteCommand(
            $"create table if not exists {_migrationsTable} (version integer primary key, applied_at_utc text not null);",
            connection))
        {
            await versionTable.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await GetSchemaVersionAsync(connection, cancellationToken) >= CurrentSchemaVersion)
        {
            return;
        }

        // SQLite's single-writer transaction (BEGIN IMMEDIATE) serializes concurrent
        // migrators, and its transactional DDL makes the migration atomic.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var version = await GetSchemaVersionAsync(connection, cancellationToken, transaction);

        if (version < 1)
        {
            await using (var migrate = new SqliteCommand(BuildInitMigrationSql(), connection, transaction))
            {
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }

            await EnsureAllowConcurrentRunsColumnAsync(connection, transaction, cancellationToken);
            await RecordSchemaVersionAsync(connection, transaction, 1, cancellationToken);
        }

        if (version < 2)
        {
            await using (var drop = new SqliteCommand($"drop table if exists {_locksTable};", connection, transaction))
            {
                await drop.ExecuteNonQueryAsync(cancellationToken);
            }

            await RecordSchemaVersionAsync(connection, transaction, 2, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var record = new SqliteCommand(
            $"insert or ignore into {_migrationsTable} (version, applied_at_utc) values (@version, @applied_at_utc);",
            connection,
            transaction);
        record.Parameters.AddWithValue("@version", version);
        record.Parameters.AddWithValue("@applied_at_utc", ToDbTimestamp(DateTimeOffset.UtcNow));
        await record.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = new SqliteCommand(
            $"select coalesce(max(version), 0) from {_migrationsTable};",
            connection,
            transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params SqliteParameter[] parameters)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqliteCommand(sql, connection);
        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobRunRecord MapRun(SqliteDataReader reader)
    {
        return new JobRunRecord
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            JobName = reader.GetString(reader.GetOrdinal("job_name")),
            JobType = reader.GetString(reader.GetOrdinal("job_type")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            PayloadJson = reader.IsDBNull(reader.GetOrdinal("payload_json"))
                ? null
                : reader.GetString(reader.GetOrdinal("payload_json")),
            ScheduledForUtc = AsUtcDateTimeOffset(reader, "scheduled_for_utc"),
            ScheduleSlotUtc = AsOptionalUtcDateTimeOffset(reader, "schedule_slot_utc"),
            StartedAtUtc = reader.IsDBNull(reader.GetOrdinal("started_at_utc"))
                ? null
                : AsUtcDateTimeOffset(reader, "started_at_utc"),
            CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("completed_at_utc"))
                ? null
                : AsUtcDateTimeOffset(reader, "completed_at_utc"),
            Attempt = reader.GetInt32(reader.GetOrdinal("attempt")),
            MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
            LeaseOwner = reader.IsDBNull(reader.GetOrdinal("lease_owner"))
                ? null
                : reader.GetString(reader.GetOrdinal("lease_owner")),
            LeaseUntilUtc = reader.IsDBNull(reader.GetOrdinal("lease_until_utc"))
                ? null
                : AsUtcDateTimeOffset(reader, "lease_until_utc"),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message"))
                ? null
                : reader.GetString(reader.GetOrdinal("error_message")),
        };
    }

    private static DateTimeOffset AsUtcDateTimeOffset(SqliteDataReader reader, string column)
    {
        var value = reader.GetString(reader.GetOrdinal(column));
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static DateTimeOffset? AsOptionalUtcDateTimeOffset(SqliteDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal);
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static int? AsOptionalInt32(SqliteDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetInt32(ordinal);
    }

    private static Core.Models.RetryBehavior? ParseRetryBehavior(SqliteDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal);
        return value switch
        {
            "fixed_delay" => Core.Models.RetryBehavior.FixedDelay,
            "backoff" => Core.Models.RetryBehavior.Backoff,
            _ => null,
        };
    }

    private static string? ToRetryBehaviorValue(Core.Models.RetryBehavior? retryBehavior)
    {
        return retryBehavior switch
        {
            Core.Models.RetryBehavior.FixedDelay => "fixed_delay",
            Core.Models.RetryBehavior.Backoff => "backoff",
            _ => null,
        };
    }

    private static int TryGetOrdinal(SqliteDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string BuildInitMigrationSql()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"create table if not exists {_jobsTable} (");
        sb.AppendLine("    id text primary key,");
        sb.AppendLine("    name text not null unique,");
        sb.AppendLine("    job_type text not null,");
        sb.AppendLine("    schedule_type text not null,");
        sb.AppendLine("    cron_expression text null,");
        sb.AppendLine("    time_zone text null,");
        sb.AppendLine("    enabled integer not null default 1,");
        sb.AppendLine("    allow_concurrent_runs integer not null default 0,");
        sb.AppendLine("    retry_behavior text null,");
        sb.AppendLine("    retry_initial_delay_seconds integer null,");
        sb.AppendLine("    payload_json text null,");
        sb.AppendLine("    max_attempts integer not null default 3,");
        sb.AppendLine("    next_run_at_utc text null,");
        sb.AppendLine("    last_run_at_utc text null,");
        sb.AppendLine("    created_at_utc text not null,");
        sb.AppendLine("    updated_at_utc text not null");
        sb.AppendLine(");");

        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{Unquote(_jobsTable)}_due")} on {_jobsTable} (enabled, schedule_type, next_run_at_utc);");

        sb.AppendLine($"create table if not exists {_runsTable} (");
        sb.AppendLine("    id text primary key,");
        sb.AppendLine($"    job_id text null references {_jobsTable}(id),");
        sb.AppendLine("    job_name text not null,");
        sb.AppendLine("    job_type text not null,");
        sb.AppendLine("    status text not null,");
        sb.AppendLine("    payload_json text null,");
        sb.AppendLine("    scheduled_for_utc text not null,");
        sb.AppendLine("    schedule_slot_utc text null,");
        sb.AppendLine("    started_at_utc text null,");
        sb.AppendLine("    completed_at_utc text null,");
        sb.AppendLine("    attempt integer not null default 0,");
        sb.AppendLine("    max_attempts integer not null default 3,");
        sb.AppendLine("    lease_owner text null,");
        sb.AppendLine("    lease_until_utc text null,");
        sb.AppendLine("    error_message text null,");
        sb.AppendLine("    error_detail text null,");
        sb.AppendLine("    created_at_utc text not null,");
        sb.AppendLine("    updated_at_utc text not null");
        sb.AppendLine(");");

        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_due")} on {_runsTable} (status, scheduled_for_utc);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_lease")} on {_runsTable} (lease_until_utc);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_job_name")} on {_runsTable} (job_name);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_completed")} on {_runsTable} (status, completed_at_utc);");
        sb.AppendLine($"create unique index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_recurring_slot_unique")} on {_runsTable} (job_name, schedule_slot_utc) where schedule_slot_utc is not null;");

        return sb.ToString();
    }

    private async Task EnsureAllowConcurrentRunsColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await HasColumnAsync(connection, transaction, "allow_concurrent_runs", cancellationToken))
        {
            var sql = $"alter table {_jobsTable} add column allow_concurrent_runs integer not null default 0;";
            await using var command = new SqliteCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, transaction, "retry_behavior", cancellationToken))
        {
            var sql = $"alter table {_jobsTable} add column retry_behavior text null;";
            await using var command = new SqliteCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, transaction, "retry_initial_delay_seconds", cancellationToken))
        {
            var sql = $"alter table {_jobsTable} add column retry_initial_delay_seconds integer null;";
            await using var command = new SqliteCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await HasColumnAsync(connection, transaction, "retry_backoff_seconds", cancellationToken))
        {
            var sql = $"alter table {_jobsTable} drop column retry_backoff_seconds;";
            await using var command = new SqliteCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<bool> HasColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string columnName, CancellationToken cancellationToken)
    {
        var pragmaSql = $"PRAGMA table_info({_jobsTable});";
        await using var pragma = new SqliteCommand(pragmaSql, connection, transaction);
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToDbTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Quote(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteIndex(string identifier)
    {
        return Quote(identifier);
    }

    private static string Unquote(string quotedIdentifier)
    {
        return quotedIdentifier.Trim('"');
    }
}
