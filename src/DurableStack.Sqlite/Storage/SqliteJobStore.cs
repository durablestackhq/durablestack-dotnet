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

public sealed class SqliteJobStore : IDurableJobStore
{
    private readonly string _connectionString;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;

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

    public async Task MarkSucceededAsync(Guid runId, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

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
            where id = @id;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@id", runId.ToString()),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));
    }

    public async Task MarkFailedAsync(
        Guid runId,
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
                where id = @id;
                """;

            parameters =
            [
                new SqliteParameter("@id", runId.ToString()),
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
                where id = @id;
                """;

            parameters =
            [
                new SqliteParameter("@id", runId.ToString()),
                new SqliteParameter("@error_message", exception.Message),
                new SqliteParameter("@error_detail", exception.ToString()),
                new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)),
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
                @now_utc,
                @now_utc)
            on conflict(name) do update set
                job_type = excluded.job_type,
                schedule_type = 'cron',
                cron_expression = excluded.cron_expression,
                time_zone = excluded.time_zone,
                enabled = 1,
                max_attempts = excluded.max_attempts,
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
            new SqliteParameter("@max_attempts", registration.MaxAttempts),
            new SqliteParameter("@next_run_at_utc", ToDbTimestamp(nextRunAtUtc)),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)));
    }

    public async Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select
                name,
                cron_expression,
                time_zone,
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
                CronExpression = reader.GetString(reader.GetOrdinal("cron_expression")),
                TimeZone = reader.GetString(reader.GetOrdinal("time_zone")),
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
              and next_run_at_utc = @expected_next_run_at_utc;
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

    public async Task ExtendLeaseAsync(
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

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqliteParameter("@lease_until_utc", ToDbTimestamp(leaseUntilUtc)),
            new SqliteParameter("@now_utc", ToDbTimestamp(nowUtc)),
            new SqliteParameter("@id", runId.ToString()),
            new SqliteParameter("@worker_name", workerName));
    }

    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        var migrationSql = BuildInitMigrationSql();
        await ExecuteNonQueryAsync(migrationSql, cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params SqliteParameter[] parameters)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqliteCommand(sql, connection);
        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
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
        sb.AppendLine("    payload_json text null,");
        sb.AppendLine("    max_attempts integer not null default 3,");
        sb.AppendLine("    retry_backoff_seconds integer not null default 60,");
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
        sb.AppendLine($"create unique index if not exists {QuoteIndex($"ix_{Unquote(_runsTable)}_recurring_slot_unique")} on {_runsTable} (job_name, schedule_slot_utc) where schedule_slot_utc is not null;");

        sb.AppendLine($"create table if not exists {_locksTable} (");
        sb.AppendLine("    lock_key text primary key,");
        sb.AppendLine("    owner text not null,");
        sb.AppendLine("    lease_until_utc text not null,");
        sb.AppendLine("    updated_at_utc text not null");
        sb.AppendLine(");");

        return sb.ToString();
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
