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
    private readonly string _connectionString;
    private readonly string _jobsTableName;
    private readonly string _runsTableName;
    private readonly string _locksTableName;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;

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

        var ids = new List<string>();
        var selectSql = $"""
            select id
            from {_runsTable}
            where (
                status = 'pending'
                and scheduled_for_utc <= @now_utc)
               or (
                status = 'leased'
                and lease_until_utc is not null
                and lease_until_utc <= @now_utc)
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

    public async Task MarkSucceededAsync(Guid runId, CancellationToken cancellationToken)
    {
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
            where id = @id;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@id", runId.ToString()));
    }

    public async Task MarkFailedAsync(
        Guid runId,
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
                where id = @id;
                """;

            parameters =
            [
                new MySqlParameter("@id", runId.ToString()),
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
                where id = @id;
                """;

            parameters =
            [
                new MySqlParameter("@id", runId.ToString()),
                new MySqlParameter("@error_message", exception.Message),
                new MySqlParameter("@error_detail", exception.ToString()),
            ];
        }

        await ExecuteNonQueryAsync(sql, cancellationToken, parameters.ToArray());
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
                enabled,
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
                Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
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
                1,
                @max_attempts,
                @next_run_at_utc,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6))
            on duplicate key update
                job_type = values(job_type),
                schedule_type = 'cron',
                cron_expression = values(cron_expression),
                time_zone = values(time_zone),
                enabled = 1,
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
                enabled,
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
                Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
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

        var updateSql = $"""
            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                last_run_at_utc = @scheduled_for_utc,
                updated_at_utc = UTC_TIMESTAMP(6)
            where name = @name
              and enabled = 1
              and schedule_type = 'cron'
              and next_run_at_utc = @expected_next_run_at_utc;
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
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
    }

    public async Task ExtendLeaseAsync(
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

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new MySqlParameter("@lease_until_utc", leaseUntilUtc.UtcDateTime),
            new MySqlParameter("@id", runId.ToString()),
            new MySqlParameter("@worker_name", workerName));
    }

    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        var migrationSql = BuildInitMigrationSql();
        await ExecuteNonQueryAsync(migrationSql, cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params MySqlParameter[] parameters)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);
        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobRunRecord MapRun(MySqlDataReader reader)
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

    private string BuildInitMigrationSql()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"create table if not exists {_jobsTable} (");
        sb.AppendLine("    id char(36) primary key,");
        sb.AppendLine("    name varchar(256) not null unique,");
        sb.AppendLine("    job_type varchar(2048) not null,");
        sb.AppendLine("    schedule_type varchar(32) not null,");
        sb.AppendLine("    cron_expression varchar(128) null,");
        sb.AppendLine("    time_zone varchar(128) null,");
        sb.AppendLine("    enabled tinyint(1) not null default 1,");
        sb.AppendLine("    payload_json longtext null,");
        sb.AppendLine("    max_attempts int not null default 3,");
        sb.AppendLine("    retry_backoff_seconds int not null default 60,");
        sb.AppendLine("    next_run_at_utc datetime(6) null,");
        sb.AppendLine("    last_run_at_utc datetime(6) null,");
        sb.AppendLine("    created_at_utc datetime(6) not null default utc_timestamp(6),");
        sb.AppendLine("    updated_at_utc datetime(6) not null default utc_timestamp(6)");
        sb.AppendLine(");");

        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{_jobsTableName}_due")} on {_jobsTable} (enabled, schedule_type, next_run_at_utc);");

        sb.AppendLine($"create table if not exists {_runsTable} (");
        sb.AppendLine("    id char(36) primary key,");
        sb.AppendLine($"    job_id char(36) null references {_jobsTable}(id),");
        sb.AppendLine("    job_name varchar(256) not null,");
        sb.AppendLine("    job_type varchar(2048) not null,");
        sb.AppendLine("    status varchar(32) not null,");
        sb.AppendLine("    payload_json longtext null,");
        sb.AppendLine("    scheduled_for_utc datetime(6) not null,");
        sb.AppendLine("    schedule_slot_utc datetime(6) null,");
        sb.AppendLine("    started_at_utc datetime(6) null,");
        sb.AppendLine("    completed_at_utc datetime(6) null,");
        sb.AppendLine("    attempt int not null default 0,");
        sb.AppendLine("    max_attempts int not null default 3,");
        sb.AppendLine("    lease_owner varchar(256) null,");
        sb.AppendLine("    lease_until_utc datetime(6) null,");
        sb.AppendLine("    error_message longtext null,");
        sb.AppendLine("    error_detail longtext null,");
        sb.AppendLine("    created_at_utc datetime(6) not null default utc_timestamp(6),");
        sb.AppendLine("    updated_at_utc datetime(6) not null default utc_timestamp(6)");
        sb.AppendLine(");");

        sb.AppendLine($"alter table {_runsTable} add column if not exists schedule_slot_utc datetime(6) null;");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{_runsTableName}_due")} on {_runsTable} (status, scheduled_for_utc);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{_runsTableName}_lease")} on {_runsTable} (lease_until_utc);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{_runsTableName}_job_name")} on {_runsTable} (job_name);");
        sb.AppendLine($"create index if not exists {QuoteIndex($"ix_{_runsTableName}_completed")} on {_runsTable} (status, completed_at_utc);");
        sb.AppendLine($"create unique index if not exists {QuoteIndex($"ix_{_runsTableName}_recurring_slot_unique")} on {_runsTable} (job_name, schedule_slot_utc);");

        sb.AppendLine($"create table if not exists {_locksTable} (");
        sb.AppendLine("    lock_key varchar(256) primary key,");
        sb.AppendLine("    owner varchar(256) not null,");
        sb.AppendLine("    lease_until_utc datetime(6) not null,");
        sb.AppendLine("    updated_at_utc datetime(6) not null default utc_timestamp(6)");
        sb.AppendLine(");");

        return sb.ToString();
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
