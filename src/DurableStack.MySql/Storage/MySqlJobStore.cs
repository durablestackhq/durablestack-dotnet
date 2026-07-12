using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using MySqlConnector;

namespace DurableStack.MySql.Storage;

public sealed class MySqlJobStore : IDurableJobStore
{
    private const string ExhaustedLeaseErrorMessage =
        "Lease expired with no attempts remaining; the worker likely crashed during execution.";

    // v1: initial schema. v2: drop the never-used job_locks table (GET_LOCK is used
    // instead).
    private const int CurrentSchemaVersion = 2;

    private readonly string _connectionString;
    private readonly string _jobsTableName;
    private readonly string _runsTableName;
    private readonly string _locksTableName;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;
    private readonly string _migrationsTable;

    public MySqlJobStore(DurableStackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.MySql.ConnectionString))
        {
            throw new ArgumentException("MySQL connection string is required.", nameof(options));
        }

        _connectionString = options.MySql.ConnectionString;
        _jobsTableName = MySqlTableNameResolver.Jobs(options);
        _runsTableName = MySqlTableNameResolver.Runs(options);
        _locksTableName = MySqlTableNameResolver.Locks(options);
        _jobsTable = Quote(_jobsTableName);
        _runsTable = Quote(_runsTableName);
        _locksTable = Quote(_locksTableName);
        _migrationsTable = Quote(MySqlTableNameResolver.Migrations(options));
    }

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
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6));
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@id", runId.ToString()),
            new MySqlParameter("@job_name", jobName),
            new MySqlParameter("@job_type", jobType),
            new MySqlParameter("@payload_json", (object?)payloadJson ?? DBNull.Value),
            new MySqlParameter("@scheduled_for_utc", scheduledForUtc.UtcDateTime),
            new MySqlParameter("@max_attempts", maxAttempts));

        return runId;
    }

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
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            from dual
            where not exists (
                select 1
                from {_runsTable}
                where job_name = @job_name
                  and status in ('pending', 'leased'));
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());
        command.Parameters.AddWithValue("@job_name", jobName);
        command.Parameters.AddWithValue("@job_type", jobType);
        command.Parameters.AddWithValue("@payload_json", (object?)payloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@scheduled_for_utc", scheduledForUtc.UtcDateTime);
        command.Parameters.AddWithValue("@max_attempts", maxAttempts);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);

        return affected > 0 ? runId : null;
    }

    public async Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var leaseUntilUtc = nowUtc.Add(leaseDuration);
        var claimed = new List<JobRunRecord>();
        var limit = Math.Max(1, batchSize);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Quarantine poison runs: a run whose lease expired with no attempts left
        // (worker crashed mid-execution) is failed terminally instead of being
        // reclaimed and crash-looping forever.
        var reapSql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = UTC_TIMESTAMP(6),
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                error_detail = null,
                updated_at_utc = UTC_TIMESTAMP(6)
            where status = 'leased'
              and lease_until_utc is not null
              and lease_until_utc <= @now_utc
              and attempt >= max_attempts;
            """;

        await using (var reap = new MySqlCommand(reapSql, connection, transaction))
        {
            reap.Parameters.AddWithValue("@error_message", ExhaustedLeaseErrorMessage);
            reap.Parameters.AddWithValue("@now_utc", nowUtc.UtcDateTime);
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
            limit {limit}
            for update skip locked;
            """;

        await using (var select = new MySqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("@now_utc", nowUtc.UtcDateTime);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // GetGuid works under both MySqlConnector Guid mappings for char(36)
                // (native Guid by default, string when GuidFormat=None).
                ids.Add(reader.GetGuid(0).ToString());
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
                updated_at_utc = UTC_TIMESTAMP(6)
            where id in ({placeholders})
              and (
                (status = 'pending' and scheduled_for_utc <= @now_utc)
                or (status = 'leased' and lease_until_utc is not null and lease_until_utc <= @now_utc));
            """;

        await using (var update = new MySqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("@worker_name", workerName);
            update.Parameters.AddWithValue("@lease_until_utc", leaseUntilUtc.UtcDateTime);
            update.Parameters.AddWithValue("@now_utc", nowUtc.UtcDateTime);

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
              and status = 'leased';
            """;

        await using (var fetch = new MySqlCommand(fetchSql, connection, transaction))
        {
            fetch.Parameters.AddWithValue("@worker_name", workerName);

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

    public async Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken)
    {
        // Fenced write: only the current lease owner may record the outcome, so a
        // worker whose lease was reclaimed cannot overwrite the new owner's state.
        var sql = $"""
            update {_runsTable}
            set
                status = 'succeeded',
                completed_at_utc = UTC_TIMESTAMP(6),
                lease_owner = null,
                lease_until_utc = null,
                error_message = null,
                error_detail = null,
                updated_at_utc = UTC_TIMESTAMP(6)
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@id", runId.ToString()),
            new MySqlParameter("@worker_name", workerName));
        return affected > 0;
    }

    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_runsTable}
            set
                status = 'failed',
                completed_at_utc = UTC_TIMESTAMP(6),
                lease_owner = null,
                lease_until_utc = null,
                error_message = @error_message,
                updated_at_utc = UTC_TIMESTAMP(6)
            where id = @id
              and status in ('pending', 'leased');
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());
        command.Parameters.AddWithValue("@error_message", "Run was cancelled.");

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> MarkFailedAsync(
        Guid runId,
        string workerName,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken)
    {
        string sql;
        List<MySqlParameter> parameters;

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
                    updated_at_utc = UTC_TIMESTAMP(6)
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;

            parameters =
            [
                new MySqlParameter("@id", runId.ToString()),
                new MySqlParameter("@worker_name", workerName),
                new MySqlParameter("@retry_at_utc", retryAtUtc.Value.UtcDateTime),
                new MySqlParameter("@error_message", exception.Message),
                new MySqlParameter("@error_detail", exception.ToString()),
            ];
        }
        else
        {
            sql = $"""
                update {_runsTable}
                set
                    status = 'failed',
                    completed_at_utc = UTC_TIMESTAMP(6),
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    updated_at_utc = UTC_TIMESTAMP(6)
                where id = @id
                  and status = 'leased'
                  and lease_owner = @worker_name;
                """;

            parameters =
            [
                new MySqlParameter("@id", runId.ToString()),
                new MySqlParameter("@worker_name", workerName),
                new MySqlParameter("@error_message", exception.Message),
                new MySqlParameter("@error_detail", exception.ToString()),
            ];
        }

        var affected = await ExecuteNonQueryAsync(sql, cancellationToken, parameters.ToArray());
        return affected > 0;
    }

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

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapRun(reader);
        }

        return null;
    }

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

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, take);
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
            limit {limit};
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@job_name", jobName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, take);
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
            limit {limit};
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, take);
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
            limit {limit};
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@status", status);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, take);
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
            limit {limit};
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

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
              and (@include_disabled or enabled = 1)
            order by name asc;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@include_disabled", includeDisabled);

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
                updated_at_utc = UTC_TIMESTAMP(6)
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@enabled", enabled);
        command.Parameters.AddWithValue("@next_run_at_utc", (object?)nextRunAtUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@name", jobName);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

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
                updated_at_utc = UTC_TIMESTAMP(6)
            where name = @name
              and schedule_type = 'cron';
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@cron_expression", cronExpression);
        command.Parameters.AddWithValue("@time_zone", timeZone);
        command.Parameters.AddWithValue("@next_run_at_utc", nextRunAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@name", jobName);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, batchSize);
        var sql = $"""
            delete from {_runsTable}
            where status in ('succeeded', 'failed')
              and completed_at_utc is not null
              and completed_at_utc < @completed_before_utc
            order by completed_at_utc asc
            limit {limit};
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@completed_before_utc", completedBeforeUtc.UtcDateTime);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6))
            on duplicate key update
                job_type = values(job_type),
                schedule_type = 'cron',
                cron_expression = values(cron_expression),
                time_zone = values(time_zone),
                enabled = values(enabled),
                allow_concurrent_runs = values(allow_concurrent_runs),
                retry_behavior = values(retry_behavior),
                retry_initial_delay_seconds = values(retry_initial_delay_seconds),
                max_attempts = values(max_attempts),
                next_run_at_utc = values(next_run_at_utc),
                updated_at_utc = UTC_TIMESTAMP(6);
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@id", Guid.NewGuid().ToString()),
            new MySqlParameter("@name", registration.JobName),
            new MySqlParameter("@job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name),
            new MySqlParameter("@cron_expression", registration.CronExpression),
            new MySqlParameter("@time_zone", registration.TimeZone),
            new MySqlParameter("@enabled", registration.Enabled),
            new MySqlParameter("@allow_concurrent_runs", registration.AllowConcurrentRuns),
            new MySqlParameter("@retry_behavior", (object?)ToRetryBehaviorValue(registration.RetryBehavior) ?? DBNull.Value),
            new MySqlParameter("@retry_initial_delay_seconds", (object?)registration.RetryInitialDelaySeconds ?? DBNull.Value),
            new MySqlParameter("@max_attempts", registration.MaxAttempts),
            new MySqlParameter("@next_run_at_utc", nextRunAtUtc.UtcDateTime));
    }

    public async Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, batchSize);
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
            limit {limit};
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@now_utc", nowUtc.UtcDateTime);

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

    public async Task UpdateRecurringNextRunAsync(
        string jobName,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                updated_at_utc = UTC_TIMESTAMP(6)
            where name = @name;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@next_run_at_utc", nextRunAtUtc.UtcDateTime),
            new MySqlParameter("@name", jobName));
    }

    public async Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Serialize competing materializers on the schedule row before the guarded
        // update below. Without this, two transactions interleave gap locks on the runs
        // table (taken by the NOT EXISTS subquery) with the jobs-row lock and deadlock.
        var lockSql = $"select id from {_jobsTable} where name = @name for update;";
        await using (var lockCommand = new MySqlCommand(lockSql, connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("@name", recurring.JobName);
            _ = await lockCommand.ExecuteScalarAsync(cancellationToken);
        }

        var updateSql = $"""
            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                last_run_at_utc = @scheduled_for_utc,
                updated_at_utc = UTC_TIMESTAMP(6)
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

        await using (var update = new MySqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("@next_run_at_utc", nextRunAtUtc.UtcDateTime);
            update.Parameters.AddWithValue("@scheduled_for_utc", recurring.NextRunAtUtc.UtcDateTime);
            update.Parameters.AddWithValue("@name", recurring.JobName);
            update.Parameters.AddWithValue("@expected_next_run_at_utc", recurring.NextRunAtUtc.UtcDateTime);

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
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6));
            """;

        await using var insert = new MySqlCommand(insertSql, connection, transaction);
        insert.Parameters.AddWithValue("@run_id", runId.ToString());
        insert.Parameters.AddWithValue("@job_name", registration.JobName);
        insert.Parameters.AddWithValue("@job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name);
        insert.Parameters.AddWithValue("@scheduled_for_utc", recurring.NextRunAtUtc.UtcDateTime);
        insert.Parameters.AddWithValue("@max_attempts", registration.MaxAttempts);

        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (MySqlException ex) when (ex.Number is 1062 or 1213)
        {
            // 1062: another worker materialized the slot (unique index).
            // 1213: deadlock victim — the competing worker wins the slot.
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
    }

    public async Task<bool> ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var leaseUntilUtc = DateTimeOffset.UtcNow.Add(leaseDuration);

        var sql = $"""
            update {_runsTable}
            set
                lease_until_utc = @lease_until_utc,
                updated_at_utc = UTC_TIMESTAMP(6)
            where id = @id
              and status = 'leased'
              and lease_owner = @worker_name;
            """;

        var affected = await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@lease_until_utc", leaseUntilUtc.UtcDateTime),
            new MySqlParameter("@id", runId.ToString()),
            new MySqlParameter("@worker_name", workerName));
        return affected > 0;
    }

    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteOnConnectionAsync(
            connection,
            $"create table if not exists {_migrationsTable} (version int primary key, applied_at_utc datetime(6) not null default (utc_timestamp(6)));",
            cancellationToken);

        if (await GetSchemaVersionAsync(connection, cancellationToken) >= CurrentSchemaVersion)
        {
            return;
        }

        // GET_LOCK serializes concurrent workers booting at once. MySQL DDL commits
        // implicitly (no transactional DDL), so the statements themselves must stay
        // individually idempotent: each runs one at a time, tolerating
        // "already exists"/"doesn't exist" errors, because MySQL (unlike MariaDB) has
        // no IF [NOT] EXISTS support for CREATE INDEX / ADD COLUMN / DROP COLUMN.
        var lockName = $"durablestack_migration_{_runsTableName}";
        await using (var lockCommand = new MySqlCommand("select get_lock(@name, 60);", connection))
        {
            lockCommand.Parameters.AddWithValue("@name", lockName);
            var acquired = await lockCommand.ExecuteScalarAsync(cancellationToken);
            if (Convert.ToInt32(acquired, System.Globalization.CultureInfo.InvariantCulture) != 1)
            {
                throw new TimeoutException("Timed out acquiring the DurableStack migration lock.");
            }
        }

        try
        {
            var version = await GetSchemaVersionAsync(connection, cancellationToken);

            if (version < 1)
            {
                await ExecuteMigrationStepAsync(connection, BuildInitMigrationStatements(), 1, cancellationToken);
            }

            if (version < 2)
            {
                await ExecuteMigrationStepAsync(connection, new[] { $"drop table if exists {_locksTable};" }, 2, cancellationToken);
            }
        }
        finally
        {
            await using var releaseCommand = new MySqlCommand("select release_lock(@name);", connection);
            releaseCommand.Parameters.AddWithValue("@name", lockName);
            _ = await releaseCommand.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private async Task ExecuteMigrationStepAsync(
        MySqlConnection connection,
        IReadOnlyList<string> statements,
        int version,
        CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            await using var command = new MySqlCommand(statement, connection);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (MySqlException ex) when (ex.Number is 1060 or 1061 or 1091)
            {
                // 1060 duplicate column, 1061 duplicate index, 1091 can't drop
                // (absent): the schema is already in the desired state.
            }
        }

        await using var record = new MySqlCommand(
            $"insert ignore into {_migrationsTable} (version, applied_at_utc) values (@version, utc_timestamp(6));",
            connection);
        record.Parameters.AddWithValue("@version", version);
        await record.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteOnConnectionAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetSchemaVersionAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand($"select coalesce(max(version), 0) from {_migrationsTable};", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);
        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobRunRecord MapRun(MySqlDataReader reader)
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

    private static DateTimeOffset AsUtcDateTimeOffset(MySqlDataReader reader, string column)
    {
        var value = reader.GetDateTime(reader.GetOrdinal(column));
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static DateTimeOffset? AsOptionalUtcDateTimeOffset(MySqlDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static int? AsOptionalInt32(MySqlDataReader reader, string column)
    {
        var ordinal = TryGetOrdinal(reader, column);
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetInt32(ordinal);
    }

    private static Core.Models.RetryBehavior? ParseRetryBehavior(MySqlDataReader reader, string column)
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

    private static int TryGetOrdinal(MySqlDataReader reader, string column)
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

    private IReadOnlyList<string> BuildInitMigrationStatements()
    {
        // Only syntax valid on both MySQL 8.x and MariaDB may be used here: expression
        // defaults must be parenthesized, and IF [NOT] EXISTS is unavailable for
        // CREATE INDEX / ADD COLUMN / DROP COLUMN (the executor tolerates the
        // corresponding duplicate/absent errors instead).
        return new[]
        {
            $"""
            create table if not exists {_jobsTable} (
                id char(36) primary key,
                name varchar(256) not null unique,
                job_type varchar(2048) not null,
                schedule_type varchar(32) not null,
                cron_expression varchar(128) null,
                time_zone varchar(128) null,
                enabled tinyint(1) not null default 1,
                allow_concurrent_runs tinyint(1) not null default 0,
                retry_behavior varchar(32) null,
                retry_initial_delay_seconds int null,
                payload_json longtext null,
                max_attempts int not null default 3,
                next_run_at_utc datetime(6) null,
                last_run_at_utc datetime(6) null,
                created_at_utc datetime(6) not null default (utc_timestamp(6)),
                updated_at_utc datetime(6) not null default (utc_timestamp(6))
            );
            """,
            $"create index {QuoteIndex($"ix_{_jobsTableName}_due")} on {_jobsTable} (enabled, schedule_type, next_run_at_utc);",
            $"alter table {_jobsTable} add column allow_concurrent_runs tinyint(1) not null default 0;",
            $"alter table {_jobsTable} add column retry_behavior varchar(32) null;",
            $"alter table {_jobsTable} add column retry_initial_delay_seconds int null;",
            $"alter table {_jobsTable} drop column retry_backoff_seconds;",
            $"""
            create table if not exists {_runsTable} (
                id char(36) primary key,
                job_id char(36) null references {_jobsTable}(id),
                job_name varchar(256) not null,
                job_type varchar(2048) not null,
                status varchar(32) not null,
                payload_json longtext null,
                scheduled_for_utc datetime(6) not null,
                schedule_slot_utc datetime(6) null,
                started_at_utc datetime(6) null,
                completed_at_utc datetime(6) null,
                attempt int not null default 0,
                max_attempts int not null default 3,
                lease_owner varchar(256) null,
                lease_until_utc datetime(6) null,
                error_message longtext null,
                error_detail longtext null,
                created_at_utc datetime(6) not null default (utc_timestamp(6)),
                updated_at_utc datetime(6) not null default (utc_timestamp(6))
            );
            """,
            $"alter table {_runsTable} add column schedule_slot_utc datetime(6) null;",
            $"create index {QuoteIndex($"ix_{_runsTableName}_due")} on {_runsTable} (status, scheduled_for_utc);",
            $"create index {QuoteIndex($"ix_{_runsTableName}_lease")} on {_runsTable} (lease_until_utc);",
            $"create index {QuoteIndex($"ix_{_runsTableName}_job_name")} on {_runsTable} (job_name);",
            $"create index {QuoteIndex($"ix_{_runsTableName}_completed")} on {_runsTable} (status, completed_at_utc);",
            $"create unique index {QuoteIndex($"ix_{_runsTableName}_recurring_slot_unique")} on {_runsTable} (job_name, schedule_slot_utc);",
        };
    }

    private static string Quote(string identifier)
    {
        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    private static string QuoteIndex(string identifier)
    {
        return Quote(identifier);
    }
}
