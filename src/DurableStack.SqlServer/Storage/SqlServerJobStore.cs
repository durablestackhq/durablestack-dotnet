using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Data.SqlClient;

namespace DurableStack.SqlServer.Storage;

public sealed class SqlServerJobStore : IDurableJobStore
{
    private readonly string _connectionString;
    private readonly string _jobsTableName;
    private readonly string _runsTableName;
    private readonly string _locksTableName;
    private readonly string _jobsTable;
    private readonly string _runsTable;
    private readonly string _locksTable;

    public SqlServerJobStore(DurableStackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = options.SqlServer.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("SQL Server connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _jobsTableName = SqlServerTableNameResolver.Jobs(options);
        _runsTableName = SqlServerTableNameResolver.Runs(options);
        _locksTableName = SqlServerTableNameResolver.Locks(options);
        _jobsTable = Qualify(_jobsTableName);
        _runsTable = Qualify(_runsTableName);
        _locksTable = Qualify(_locksTableName);
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
                N'pending',
                @payload_json,
                @scheduled_for_utc,
                null,
                0,
                @max_attempts,
                SYSUTCDATETIME(),
                SYSUTCDATETIME());
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqlParameter("@id", runId),
            new SqlParameter("@job_name", jobName),
            new SqlParameter("@job_type", jobType),
            new SqlParameter("@payload_json", (object?)payloadJson ?? DBNull.Value),
            new SqlParameter("@scheduled_for_utc", scheduledForUtc.UtcDateTime),
            new SqlParameter("@max_attempts", maxAttempts));

        return runId;
    }

    public async Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var leaseSeconds = Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));

        var sql = $"""
            ;with due_runs as (
                select top (@batch_size) id
                from {_runsTable} with (updlock, readpast, rowlock)
                where (
                    status = N'pending'
                    and scheduled_for_utc <= SYSUTCDATETIME())
                   or (
                    status = N'leased'
                    and lease_until_utc is not null
                    and lease_until_utc <= SYSUTCDATETIME())
                order by scheduled_for_utc asc
            )
            update r
            set
                status = N'leased',
                lease_owner = @worker_name,
                lease_until_utc = DATEADD(second, @lease_seconds, SYSUTCDATETIME()),
                started_at_utc = ISNULL(r.started_at_utc, SYSUTCDATETIME()),
                attempt = r.attempt + 1,
                updated_at_utc = SYSUTCDATETIME()
            output
                inserted.id,
                inserted.job_name,
                inserted.job_type,
                inserted.status,
                inserted.payload_json,
                inserted.scheduled_for_utc,
                inserted.started_at_utc,
                inserted.completed_at_utc,
                inserted.attempt,
                inserted.max_attempts,
                inserted.lease_owner,
                inserted.lease_until_utc,
                inserted.error_message
            from {_runsTable} as r
            inner join due_runs on due_runs.id = r.id;
            """;

        var runs = new List<JobRunRecord>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(
        [
            new SqlParameter("@batch_size", batchSize),
            new SqlParameter("@worker_name", workerName),
            new SqlParameter("@lease_seconds", leaseSeconds),
        ]);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task MarkSucceededAsync(Guid runId, CancellationToken cancellationToken)
    {
        var sql = $"""
            update {_runsTable}
            set
                status = N'succeeded',
                completed_at_utc = SYSUTCDATETIME(),
                lease_owner = null,
                lease_until_utc = null,
                error_message = null,
                error_detail = null,
                updated_at_utc = SYSUTCDATETIME()
            where id = @id;
            """;

        await ExecuteNonQueryAsync(sql, cancellationToken, new SqlParameter("@id", runId));
    }

    public async Task MarkFailedAsync(
        Guid runId,
        Exception exception,
        bool retry,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken)
    {
        string sql;
        List<SqlParameter> parameters;

        if (retry && retryAtUtc.HasValue)
        {
            sql = $"""
                update {_runsTable}
                set
                    status = N'pending',
                    scheduled_for_utc = @retry_at_utc,
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    completed_at_utc = null,
                    updated_at_utc = SYSUTCDATETIME()
                where id = @id;
                """;

            parameters =
            [
                new SqlParameter("@id", runId),
                new SqlParameter("@retry_at_utc", retryAtUtc.Value.UtcDateTime),
                new SqlParameter("@error_message", exception.Message),
                new SqlParameter("@error_detail", exception.ToString()),
            ];
        }
        else
        {
            sql = $"""
                update {_runsTable}
                set
                    status = N'failed',
                    completed_at_utc = SYSUTCDATETIME(),
                    lease_owner = null,
                    lease_until_utc = null,
                    error_message = @error_message,
                    error_detail = @error_detail,
                    updated_at_utc = SYSUTCDATETIME()
                where id = @id;
                """;

            parameters =
            [
                new SqlParameter("@id", runId),
                new SqlParameter("@error_message", exception.Message),
                new SqlParameter("@error_detail", exception.ToString()),
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

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", runId);

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

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(MapRun(reader));
        }

        return runs;
    }

    public async Task<int> PruneHistoricalRunsAsync(
        DateTimeOffset completedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            ;with to_delete as (
                select top (@batch_size) id
                from {_runsTable} with (READPAST)
                where status in (N'succeeded', N'failed')
                  and completed_at_utc is not null
                  and completed_at_utc < @completed_before_utc
                order by completed_at_utc asc
            )
            delete r
            from {_runsTable} as r
            inner join to_delete on r.id = to_delete.id;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch_size", Math.Max(1, batchSize));
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
            update {_jobsTable}
            set
                job_type = @job_type,
                schedule_type = N'cron',
                cron_expression = @cron_expression,
                time_zone = @time_zone,
                enabled = 1,
                max_attempts = @max_attempts,
                updated_at_utc = SYSUTCDATETIME()
            where name = @name;

            if @@ROWCOUNT = 0
            begin
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
                    N'cron',
                    @cron_expression,
                    @time_zone,
                    1,
                    @max_attempts,
                    @next_run_at_utc,
                    SYSUTCDATETIME(),
                    SYSUTCDATETIME());
            end
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqlParameter("@id", Guid.NewGuid()),
            new SqlParameter("@name", registration.JobName),
            new SqlParameter("@job_type", registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name),
            new SqlParameter("@cron_expression", registration.CronExpression),
            new SqlParameter("@time_zone", registration.TimeZone),
            new SqlParameter("@max_attempts", registration.MaxAttempts),
            new SqlParameter("@next_run_at_utc", nextRunAtUtc.UtcDateTime));
    }

    public async Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            select top (@batch_size)
                name,
                cron_expression,
                time_zone,
                next_run_at_utc
            from {_jobsTable}
            where enabled = 1
              and schedule_type = N'cron'
              and next_run_at_utc is not null
              and next_run_at_utc <= @now_utc
            order by next_run_at_utc asc;
            """;

        var jobs = new List<RecurringJobState>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@now_utc", nowUtc.UtcDateTime);
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
                updated_at_utc = SYSUTCDATETIME()
            where name = @name;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqlParameter("@next_run_at_utc", nextRunAtUtc.UtcDateTime),
            new SqlParameter("@name", jobName));
    }

    public async Task<bool> TryMaterializeRecurringRunAsync(
        RecurringJobState recurring,
        DurableJobRegistration registration,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var jobType = registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name;

        var sql = $"""
            declare @claimed table (name nvarchar(256) not null);

            update {_jobsTable}
            set
                next_run_at_utc = @next_run_at_utc,
                last_run_at_utc = @scheduled_for_utc,
                updated_at_utc = SYSUTCDATETIME()
            output inserted.name into @claimed(name)
            where name = @name
              and enabled = 1
              and schedule_type = N'cron'
              and next_run_at_utc = @expected_next_run_at_utc;

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
                N'pending',
                null,
                @scheduled_for_utc,
                @scheduled_for_utc,
                0,
                @max_attempts,
                SYSUTCDATETIME(),
                SYSUTCDATETIME()
            from @claimed;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(
        [
            new SqlParameter("@next_run_at_utc", nextRunAtUtc.UtcDateTime),
            new SqlParameter("@scheduled_for_utc", recurring.NextRunAtUtc.UtcDateTime),
            new SqlParameter("@expected_next_run_at_utc", recurring.NextRunAtUtc.UtcDateTime),
            new SqlParameter("@name", recurring.JobName),
            new SqlParameter("@run_id", runId),
            new SqlParameter("@job_name", registration.JobName),
            new SqlParameter("@job_type", jobType),
            new SqlParameter("@max_attempts", registration.MaxAttempts),
        ]);

        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected > 0;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    public async Task ExtendLeaseAsync(
        Guid runId,
        string workerName,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var leaseSeconds = Math.Max(1, (int)Math.Ceiling(leaseDuration.TotalSeconds));

        var sql = $"""
            update {_runsTable}
            set
                lease_until_utc = DATEADD(second, @lease_seconds, SYSUTCDATETIME()),
                updated_at_utc = SYSUTCDATETIME()
            where id = @id
              and status = N'leased'
              and lease_owner = @worker_name;
            """;

        await ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            new SqlParameter("@id", runId),
            new SqlParameter("@worker_name", workerName),
            new SqlParameter("@lease_seconds", leaseSeconds));
    }

    public async Task EnsureMigrationsAppliedAsync(CancellationToken cancellationToken)
    {
        var migrationSql = BuildInitMigrationSql();
        await ExecuteNonQueryAsync(migrationSql, cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);
        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobRunRecord MapRun(SqlDataReader reader)
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

    private static DateTimeOffset AsUtcDateTimeOffset(SqlDataReader reader, string column)
    {
        var value = reader.GetDateTime(reader.GetOrdinal(column));
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private string BuildInitMigrationSql()
    {
        var jobsObject = TableObjectName(_jobsTableName);
        var runsObject = TableObjectName(_runsTableName);
        var locksObject = TableObjectName(_locksTableName);

        var runsDueIndexName = $"ix_{_runsTableName}_due";
        var runsLeaseIndexName = $"ix_{_runsTableName}_lease";
        var runsJobNameIndexName = $"ix_{_runsTableName}_job_name";
        var runsCompletedIndexName = $"ix_{_runsTableName}_completed";
        var runsRecurringUniqueIndexName = $"ix_{_runsTableName}_recurring_slot_unique";
        var jobsDueIndexName = $"ix_{_jobsTableName}_due";

        return $"""
            if object_id(N'{jobsObject}', N'U') is null
            begin
                create table {_jobsTable} (
                    id uniqueidentifier not null primary key,
                    name nvarchar(256) not null unique,
                    job_type nvarchar(2048) not null,
                    schedule_type nvarchar(32) not null,
                    cron_expression nvarchar(128) null,
                    time_zone nvarchar(128) null,
                    enabled bit not null constraint DF_{_jobsTableName}_enabled default 1,
                    payload_json nvarchar(max) null,
                    max_attempts int not null constraint DF_{_jobsTableName}_max_attempts default 3,
                    retry_backoff_seconds int not null constraint DF_{_jobsTableName}_retry_backoff_seconds default 60,
                    next_run_at_utc datetime2(7) null,
                    last_run_at_utc datetime2(7) null,
                    created_at_utc datetime2(7) not null constraint DF_{_jobsTableName}_created_at_utc default SYSUTCDATETIME(),
                    updated_at_utc datetime2(7) not null constraint DF_{_jobsTableName}_updated_at_utc default SYSUTCDATETIME()
                );
            end;

            if not exists (select 1 from sys.indexes where name = N'{jobsDueIndexName}' and object_id = object_id(N'{jobsObject}'))
            begin
                create index [{jobsDueIndexName}] on {_jobsTable} (enabled, schedule_type, next_run_at_utc);
            end;

            if object_id(N'{runsObject}', N'U') is null
            begin
                create table {_runsTable} (
                    id uniqueidentifier not null primary key,
                    job_id uniqueidentifier null,
                    job_name nvarchar(256) not null,
                    job_type nvarchar(2048) not null,
                    status nvarchar(32) not null,
                    payload_json nvarchar(max) null,
                    scheduled_for_utc datetime2(7) not null,
                    schedule_slot_utc datetime2(7) null,
                    started_at_utc datetime2(7) null,
                    completed_at_utc datetime2(7) null,
                    attempt int not null constraint DF_{_runsTableName}_attempt default 0,
                    max_attempts int not null constraint DF_{_runsTableName}_max_attempts default 3,
                    lease_owner nvarchar(256) null,
                    lease_until_utc datetime2(7) null,
                    error_message nvarchar(max) null,
                    error_detail nvarchar(max) null,
                    created_at_utc datetime2(7) not null constraint DF_{_runsTableName}_created_at_utc default SYSUTCDATETIME(),
                    updated_at_utc datetime2(7) not null constraint DF_{_runsTableName}_updated_at_utc default SYSUTCDATETIME(),
                    constraint FK_{_runsTableName}_job_id foreign key (job_id) references {_jobsTable} (id)
                );
            end;

            if col_length(N'{runsObject}', N'schedule_slot_utc') is null
            begin
                alter table {_runsTable} add schedule_slot_utc datetime2(7) null;
            end;

            if not exists (select 1 from sys.indexes where name = N'{runsDueIndexName}' and object_id = object_id(N'{runsObject}'))
            begin
                create index [{runsDueIndexName}] on {_runsTable} (status, scheduled_for_utc);
            end;

            if not exists (select 1 from sys.indexes where name = N'{runsLeaseIndexName}' and object_id = object_id(N'{runsObject}'))
            begin
                create index [{runsLeaseIndexName}] on {_runsTable} (lease_until_utc);
            end;

            if not exists (select 1 from sys.indexes where name = N'{runsJobNameIndexName}' and object_id = object_id(N'{runsObject}'))
            begin
                create index [{runsJobNameIndexName}] on {_runsTable} (job_name);
            end;

            if not exists (select 1 from sys.indexes where name = N'{runsCompletedIndexName}' and object_id = object_id(N'{runsObject}'))
            begin
                create index [{runsCompletedIndexName}] on {_runsTable} (status, completed_at_utc);
            end;

            if not exists (select 1 from sys.indexes where name = N'{runsRecurringUniqueIndexName}' and object_id = object_id(N'{runsObject}'))
            begin
                create unique index [{runsRecurringUniqueIndexName}] on {_runsTable} (job_name, schedule_slot_utc) where schedule_slot_utc is not null;
            end;

            if object_id(N'{locksObject}', N'U') is null
            begin
                create table {_locksTable} (
                    lock_key nvarchar(256) not null primary key,
                    owner nvarchar(256) not null,
                    lease_until_utc datetime2(7) not null,
                    updated_at_utc datetime2(7) not null constraint DF_{_locksTableName}_updated_at_utc default SYSUTCDATETIME()
                );
            end;
            """;
    }

    private static string Qualify(string tableName)
    {
        return $"[dbo].[{tableName.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string TableObjectName(string tableName)
    {
        return $"dbo.{tableName}";
    }
}
