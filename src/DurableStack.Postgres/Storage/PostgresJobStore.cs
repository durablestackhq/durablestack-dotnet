using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Npgsql;

namespace DurableStack.Postgres.Storage;

/// <summary>
/// PostgreSQL-backed job store. Due runs are claimed atomically in a single CTE update
/// using FOR UPDATE SKIP LOCKED, so concurrent workers never block or double-claim, and
/// leases are evaluated against the database clock (now()). Payloads are stored as
/// jsonb, which rejects invalid JSON and normalizes the stored text. Completion writes
/// are lease-fenced, and a run whose lease expired with no attempts remaining is failed
/// terminally at claim time rather than reclaimed.
/// </summary>
public sealed class PostgresJobStore : IDurableJobStore
{
    private const string ExhaustedLeaseErrorMessage =
        "Lease expired with no attempts remaining; the worker likely crashed during execution.";

    // v1: initial schema. v2: drop the never-used job_locks table (native advisory
    // locks are used instead).
    private const int CurrentSchemaVersion = 2;

    private readonly string _connectionString;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;
    private readonly string _migrationsTable;

    /// <summary>
    /// Initializes the store from the configured options. Requires a PostgreSQL connection
    /// string; table names are resolved from the configured table prefix, which is
    /// lowercased.
    /// </summary>
    public PostgresJobStore(DurableStackOptions options)
    {
        var connectionString = options.Postgres.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("PostgreSQL connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _jobsTable = PostgresTableNameResolver.Jobs(options);
        _runsTable = PostgresTableNameResolver.Runs(options);
        _locksTable = PostgresTableNameResolver.Locks(options);
        _migrationsTable = PostgresTableNameResolver.Migrations(options);
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
                cast(@payload_json as jsonb),
                @scheduled_for_utc,
                null,
                0,
                @max_attempts,
                now(),
                now());
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("job_name", jobName);
        command.Parameters.AddWithValue("job_type", jobType);
        command.Parameters.AddWithValue("payload_json", (object?)payloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("scheduled_for_utc", scheduledForUtc.UtcDateTime);
        command.Parameters.AddWithValue("max_attempts", maxAttempts);

        await command.ExecuteNonQueryAsync(cancellationToken);
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
                cast(@payload_json as jsonb),
                @scheduled_for_utc,
                null,
                0,
                @max_attempts,
                now(),
                now()
            where not exists (
                select 1
                from {_runsTable}
                where job_name = @job_name
                  and status in ('pending', 'leased'))
            returning id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("job_name", jobName);
        command.Parameters.AddWithValue("job_type", jobType);
        command.Parameters.AddWithValue("payload_json", (object?)payloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("scheduled_for_utc", scheduledForUtc.UtcDateTime);
        command.Parameters.AddWithValue("max_attempts", maxAttempts);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : runId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        // Quarantine poison runs: a run whose lease expired with no attempts left
        // (worker crashed mid-execution) is failed terminally instead of being
        // reclaimed and crash-looping forever.
        var reapSql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = now(),
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                error_detail = null,
                updated_at_utc = now()
            where status = 'leased'
              and lease_until_utc is not null
              and lease_until_utc <= now()
              and attempt >= max_attempts;
            """;

        var sql = $"""
            with due_runs as (
                select id
                from {_runsTable}
                where attempt < max_attempts
                  and ((
                    status = 'pending'
                    and scheduled_for_utc <= now())
                   or (
                    status = 'leased'
                    and lease_until_utc is not null
                    and lease_until_utc <= now()))
                order by scheduled_for_utc asc
                limit @batch_size
                for update skip locked
            )
            update {_runsTable} as r
            set
                status = 'leased',
                lease_owner = @worker_name,
                lease_until_utc = now() + @lease_duration,
                started_at_utc = coalesce(r.started_at_utc, now()),
                attempt = r.attempt + 1,
                updated_at_utc = now()
            from due_runs
            where r.id = due_runs.id
            returning
                r.id,
                r.job_name,
                r.job_type,
                r.status,
                r.payload_json,
                r.scheduled_for_utc,
                r.started_at_utc,
                r.completed_at_utc,
                r.attempt,
                r.max_attempts,
                r.lease_owner,
                r.lease_until_utc,
                r.error_message;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var reapCommand = new NpgsqlCommand(reapSql, connection, transaction))
        {
            reapCommand.Parameters.AddWithValue("error_message", ExhaustedLeaseErrorMessage);
            await reapCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("worker_name", workerName);
        command.Parameters.AddWithValue("lease_duration", leaseDuration);

        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                runs.Add(MapRun(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return runs;
    }

    /// <inheritdoc />
    public async Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken)
    {
        // Fenced write: only the current lease owner may record the outcome, so a
        // worker whose lease was reclaimed cannot overwrite the new owner's state.
        var sql = $"""
            update {_runsTable}
            set
                status = 'succeeded',
                completed_at_utc = now(),
                lease_owner = null,
                lease_until_utc = null,
                error_message = null,
                error_detail = null,
                updated_at_utc = now()
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new NpgsqlParameter("id", runId),
            new NpgsqlParameter("worker_name", workerName));
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = now(),
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                updated_at_utc = now()
            where id = @id
              and status in ('pending', 'leased');
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("error_message", "Run was cancelled.");

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
        string sql;
        List<NpgsqlParameter> parameters;

        if (retry && retryAtUtc.HasValue)
        {
            sql = """
                update {runs}
                set
                    status = 'pending',
                    scheduled_for_utc = @retry_at_utc,
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    completed_at_utc = null,
                    updated_at_utc = now()
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;
            sql = sql.Replace("{runs}", _runsTable, StringComparison.Ordinal);

            parameters =
            [
                new NpgsqlParameter("id", runId),
                new NpgsqlParameter("worker_name", workerName),
                new NpgsqlParameter("retry_at_utc", retryAtUtc.Value.UtcDateTime),
                new NpgsqlParameter("error_message", exception.Message),
                new NpgsqlParameter("error_detail", exception.ToString()),
            ];
        }
        else
        {
            sql = """
                update {runs}
                set
                    status = 'failed',
                    completed_at_utc = now(),
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    updated_at_utc = now()
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;
            sql = sql.Replace("{runs}", _runsTable, StringComparison.Ordinal);

            parameters =
            [
                new NpgsqlParameter("id", runId),
                new NpgsqlParameter("worker_name", workerName),
                new NpgsqlParameter("error_message", exception.Message),
                new NpgsqlParameter("error_detail", exception.ToString()),
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);

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
                schedule_slot_utc,
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_name", jobName);
        command.Parameters.AddWithValue("take", Math.Max(1, take));

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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("take", Math.Max(1, take));

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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("take", Math.Max(1, take));

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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("take", Math.Max(1, take));

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
              and (@include_disabled or enabled = true)
            order by name asc;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("include_disabled", includeDisabled);

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
                Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
                AllowConcurrentRuns = reader.GetBoolean(reader.GetOrdinal("allow_concurrent_runs")),
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
                updated_at_utc = now()
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("next_run_at_utc", (object?)nextRunAtUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("name", jobName);

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
                updated_at_utc = now()
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cron_expression", cronExpression);
        command.Parameters.AddWithValue("time_zone", timeZone);
        command.Parameters.AddWithValue("next_run_at_utc", nextRunAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("name", jobName);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            with to_delete as (
                select id
                from {_runsTable}
                where status in ('succeeded', 'failed')
                  and completed_at_utc is not null
                  and completed_at_utc < @completed_before_utc
                order by completed_at_utc asc
                limit @batch_size
            )
            delete from {_runsTable} as r
            using to_delete
            where r.id = to_delete.id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("completed_before_utc", completedBeforeUtc.UtcDateTime);
        command.Parameters.AddWithValue("batch_size", Math.Max(1, batchSize));

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
                now(),
                now())
            on conflict (name)
            do update
            set
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
                updated_at_utc = now();
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("name", registration.JobName),
            new NpgsqlParameter("job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name),
            new NpgsqlParameter("cron_expression", registration.CronExpression),
            new NpgsqlParameter("time_zone", registration.TimeZone),
            new NpgsqlParameter("enabled", registration.Enabled),
            new NpgsqlParameter("allow_concurrent_runs", registration.AllowConcurrentRuns),
            new NpgsqlParameter("retry_behavior", (object?)ToRetryBehaviorValue(registration.RetryBehavior) ?? DBNull.Value),
            new NpgsqlParameter("retry_initial_delay_seconds", (object?)registration.RetryInitialDelaySeconds ?? DBNull.Value),
            new NpgsqlParameter("max_attempts", registration.MaxAttempts),
            new NpgsqlParameter("next_run_at_utc", nextRunAtUtc.UtcDateTime));
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
            where enabled = true
              and schedule_type = 'cron'
              and next_run_at_utc is not null
              and next_run_at_utc <= @now_utc
            order by next_run_at_utc asc
            limit @batch_size;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("now_utc", nowUtc.UtcDateTime);
        command.Parameters.AddWithValue("batch_size", batchSize);

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
                Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
                AllowConcurrentRuns = reader.GetBoolean(reader.GetOrdinal("allow_concurrent_runs")),
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
                updated_at_utc = now()
            where name = @name;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new NpgsqlParameter("next_run_at_utc", nextRunAtUtc.UtcDateTime),
            new NpgsqlParameter("name", jobName));
    }

    /// <inheritdoc />
    public async Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var jobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name;

        var sql = $"""
            with claimed as (
                update {_jobsTable}
                set
                    next_run_at_utc = @next_run_at_utc,
                    last_run_at_utc = @scheduled_for_utc,
                    updated_at_utc = now()
                where name = @name
                  and enabled = true
                  and schedule_type = 'cron'
                  and next_run_at_utc = @expected_next_run_at_utc
                  and (
                      (
                          allow_concurrent_runs = false
                          and not exists (
                              select 1
                              from {_runsTable} active
                              where active.job_name = @name
                                and active.status in ('pending', 'leased'))
                      )
                      or (
                          allow_concurrent_runs = true
                          and not exists (
                              select 1
                              from {_runsTable} active
                              where active.job_name = @name
                                and active.status = 'pending')
                      ))
                returning name
            )
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
                @run_id,
                @job_name,
                @job_type,
                'pending',
                cast(@payload_json as jsonb),
                @scheduled_for_utc,
                @scheduled_for_utc,
                0,
                @max_attempts,
                now(),
                now()
            from claimed;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("next_run_at_utc", nextRunAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("scheduled_for_utc", recurring.NextRunAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("expected_next_run_at_utc", recurring.NextRunAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("name", recurring.JobName);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("job_name", registration.JobName);
        command.Parameters.AddWithValue("job_type", jobType);
        command.Parameters.AddWithValue("payload_json", DBNull.Value);
        command.Parameters.AddWithValue("max_attempts", registration.MaxAttempts);

        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_runsTable}
            set
                lease_until_utc = now() + @lease_duration,
                updated_at_utc = now()
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new NpgsqlParameter("id", runId),
            new NpgsqlParameter("worker_name", workerName),
            new NpgsqlParameter("lease_duration", leaseDuration));
        return affected > 0;
    }

    private async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobRunRecord MapRun(NpgsqlDataReader reader)
    {
        return new JobRunRecord
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
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

    private static DateTimeOffset AsUtcDateTimeOffset(NpgsqlDataReader reader, string column)
    {
        var value = reader.GetDateTime(reader.GetOrdinal(column));
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static DateTimeOffset? AsOptionalUtcDateTimeOffset(NpgsqlDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static int? AsOptionalInt32(NpgsqlDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetInt32(ordinal);
    }

    private static Core.Models.RetryBehavior? ParseRetryBehavior(NpgsqlDataReader reader, string column)
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

    private static int TryGetOrdinal(NpgsqlDataReader reader, string column)
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

    /// <summary>
    /// Creates or upgrades the schema to the current version, recording each applied
    /// version in the schema migrations table. Concurrent workers are serialized with a
    /// transaction-scoped advisory lock (pg_advisory_xact_lock), and PostgreSQL's
    /// transactional DDL applies each migration atomically. Returns without locking when
    /// the schema is already current.
    /// </summary>
    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureSchemaVersionTableAsync(connection, cancellationToken);
        if (await GetSchemaVersionAsync(connection, null, cancellationToken) >= CurrentSchemaVersion)
        {
            return;
        }

        // A transaction-scoped advisory lock serializes concurrent workers booting at
        // once, and PostgreSQL's transactional DDL makes the migration atomic — a
        // mid-migration crash leaves no partial schema.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("select pg_advisory_xact_lock(@key);", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", ComputeMigrationLockKey(_migrationsTable));
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var version = await GetSchemaVersionAsync(connection, transaction, cancellationToken);

        if (version < 1)
        {
            await ExecuteMigrationStepAsync(connection, transaction, BuildInitMigrationSql(), 1, cancellationToken);
        }

        if (version < 2)
        {
            await ExecuteMigrationStepAsync(connection, transaction, $"drop table if exists {_locksTable};", 2, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExecuteMigrationStepAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        int version,
        CancellationToken cancellationToken)
    {
        await using (var migrate = new NpgsqlCommand(sql, connection, transaction))
        {
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var record = new NpgsqlCommand(
            $"insert into {_migrationsTable} (version, applied_at_utc) values (@version, now()) on conflict (version) do nothing;",
            connection,
            transaction))
        {
            record.Parameters.AddWithValue("version", version);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EnsureSchemaVersionTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = $"""
            create table if not exists {_migrationsTable} (
                version int primary key,
                applied_at_utc timestamptz not null default now()
            );
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState is "23505" or "42P07" or "42710")
        {
            // Concurrent CREATE TABLE IF NOT EXISTS can still race on the catalog
            // (duplicate pg_type key / duplicate table / duplicate_object); the
            // table exists either way.
        }
    }

    private async Task<int> GetSchemaVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select coalesce(max(version), 0) from {_migrationsTable};",
            connection,
            transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long ComputeMigrationLockKey(string name)
    {
        // Stable FNV-1a 64-bit hash so every worker (and only workers sharing this
        // table prefix) contends on the same advisory lock across processes.
        var hash = 14695981039346656037UL;
        foreach (var c in $"durablestack:{name}")
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return unchecked((long)hash);
    }

    private string BuildInitMigrationSql()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"create table if not exists {_jobsTable} (");
        sb.AppendLine("    id uuid primary key,");
        sb.AppendLine("    name text not null unique,");
        sb.AppendLine("    job_type text not null,");
        sb.AppendLine("    schedule_type text not null,");
        sb.AppendLine("    cron_expression text null,");
        sb.AppendLine("    time_zone text null,");
        sb.AppendLine("    enabled boolean not null default true,");
        sb.AppendLine("    allow_concurrent_runs boolean not null default false,");
        sb.AppendLine("    retry_behavior text null,");
        sb.AppendLine("    retry_initial_delay_seconds int null,");
        sb.AppendLine("    payload_json jsonb null,");
        sb.AppendLine("    max_attempts int not null default 3,");
        sb.AppendLine("    next_run_at_utc timestamptz null,");
        sb.AppendLine("    last_run_at_utc timestamptz null,");
        sb.AppendLine("    created_at_utc timestamptz not null default now(),");
        sb.AppendLine("    updated_at_utc timestamptz not null default now()");
        sb.AppendLine(");");

        sb.AppendLine($"create index if not exists ix_{_jobsTable}_due on {_jobsTable} (enabled, schedule_type, next_run_at_utc);");

        sb.AppendLine($"create table if not exists {_runsTable} (");
        sb.AppendLine("    id uuid primary key,");
        sb.AppendLine($"    job_id uuid null references {_jobsTable}(id),");
        sb.AppendLine("    job_name text not null,");
        sb.AppendLine("    job_type text not null,");
        sb.AppendLine("    status text not null,");
        sb.AppendLine("    payload_json jsonb null,");
        sb.AppendLine("    scheduled_for_utc timestamptz not null,");
        sb.AppendLine("    schedule_slot_utc timestamptz null,");
        sb.AppendLine("    started_at_utc timestamptz null,");
        sb.AppendLine("    completed_at_utc timestamptz null,");
        sb.AppendLine("    attempt int not null default 0,");
        sb.AppendLine("    max_attempts int not null default 3,");
        sb.AppendLine("    lease_owner text null,");
        sb.AppendLine("    lease_until_utc timestamptz null,");
        sb.AppendLine("    error_message text null,");
        sb.AppendLine("    error_detail text null,");
        sb.AppendLine("    created_at_utc timestamptz not null default now(),");
        sb.AppendLine("    updated_at_utc timestamptz not null default now()");
        sb.AppendLine(");");

        sb.AppendLine($"create index if not exists ix_{_runsTable}_due on {_runsTable} (status, scheduled_for_utc);");
        sb.AppendLine($"create index if not exists ix_{_runsTable}_lease on {_runsTable} (lease_until_utc);");
        sb.AppendLine($"create index if not exists ix_{_runsTable}_job_name on {_runsTable} (job_name);");
        sb.AppendLine($"create index if not exists ix_{_runsTable}_completed on {_runsTable} (status, completed_at_utc);");
        sb.AppendLine($"alter table {_jobsTable} add column if not exists allow_concurrent_runs boolean not null default false;");
        sb.AppendLine($"alter table {_jobsTable} add column if not exists retry_behavior text null;");
        sb.AppendLine($"alter table {_jobsTable} add column if not exists retry_initial_delay_seconds int null;");
        sb.AppendLine($"alter table {_jobsTable} drop column if exists retry_backoff_seconds;");
        sb.AppendLine($"alter table {_runsTable} add column if not exists schedule_slot_utc timestamptz null;");
        sb.AppendLine($"create unique index if not exists ix_{_runsTable}_recurring_slot_unique on {_runsTable} (job_name, schedule_slot_utc) where schedule_slot_utc is not null;");

        return sb.ToString();
    }
}
