using System;

namespace DurableStack.Core.Options;

/// <summary>
/// Root configuration for DurableStack: storage provider selection, worker identity,
/// polling, leasing, concurrency, retry, and shutdown behavior, plus nested option groups
/// for eventing, recurring jobs, retention, and job registration. Bindable from
/// configuration or configured in code via the Use* methods.
/// </summary>
public sealed class DurableStackOptions
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultShutdownDrainTimeout = TimeSpan.FromSeconds(10);
    private const int DefaultClaimBatchSize = 5;
    private const int DefaultMaxConcurrentRuns = 5;
    private int _claimBatchSize = DefaultClaimBatchSize;

    /// <summary>
    /// Builds the default worker identity, "{hostname}-{process id}", which is unique per
    /// process and stable for its lifetime.
    /// </summary>
    public static string CreateDefaultWorkerName()
    {
        var host = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        return $"{host}-{Environment.ProcessId}";
    }

    /// <summary>
    /// Which backing store persists runs and schedules. Defaults to
    /// <see cref="DurableStackStorageProvider.InMemory"/>, which loses all state on process
    /// exit; pick a database provider for durability.
    /// </summary>
    public DurableStackStorageProvider StorageProvider { get; set; } = DurableStackStorageProvider.InMemory;

    /// <summary>
    /// Connection settings used when <see cref="StorageProvider"/> is
    /// <see cref="DurableStackStorageProvider.Postgres"/>.
    /// </summary>
    public PostgresDurableStackOptions Postgres { get; } = new();

    /// <summary>
    /// Connection settings used when <see cref="StorageProvider"/> is
    /// <see cref="DurableStackStorageProvider.SqlServer"/>.
    /// </summary>
    public SqlServerDurableStackOptions SqlServer { get; } = new();

    /// <summary>
    /// Connection settings used when <see cref="StorageProvider"/> is
    /// <see cref="DurableStackStorageProvider.Sqlite"/>.
    /// </summary>
    public SqliteDurableStackOptions Sqlite { get; } = new();

    /// <summary>
    /// Connection settings used when <see cref="StorageProvider"/> is
    /// <see cref="DurableStackStorageProvider.MySql"/>.
    /// </summary>
    public MySqlDurableStackOptions MySql { get; } = new();

    /// <summary>
    /// Settings for publishing job lifecycle events to the hosted DurableStack telemetry
    /// platform: credentials, batching, and error-detail redaction.
    /// </summary>
    public DurableStackEventingOptions Eventing { get; } = new();

    /// <summary>
    /// Settings for recurring (cron) jobs: what to do with occurrences missed while no
    /// worker was running, and how code registrations sync with stored schedules.
    /// </summary>
    public DurableStackRecurringOptions Recurring { get; } = new();

    /// <summary>
    /// Settings for the retention sweep that deletes old completed runs. Enabled by default.
    /// </summary>
    public DurableStackRetentionOptions Retention { get; } = new();

    /// <summary>
    /// Settings controlling how jobs are discovered and registered, such as assembly
    /// auto-discovery.
    /// </summary>
    public DurableStackJobRegistrationOptions JobRegistration { get; } = new();

    /// <summary>
    /// How job instances are resolved from dependency injection. Defaults to
    /// <see cref="DurableStackJobActivationMode.ScopedPerExecution"/>, which creates and
    /// disposes a DI scope per execution so jobs can depend on scoped services.
    /// </summary>
    public DurableStackJobActivationMode JobActivation { get; set; } = DurableStackJobActivationMode.ScopedPerExecution;

    /// <summary>
    /// Identity this process uses when claiming runs; it is recorded as the lease owner and
    /// fences completion writes. Must be unique per worker process or workers will overwrite
    /// each other's leases. Defaults to "{hostname}-{process id}".
    /// </summary>
    public string WorkerName { get; set; } = CreateDefaultWorkerName();

    /// <summary>
    /// Optional prefix prepended to DurableStack's table names, letting multiple isolated
    /// deployments share one database. Null (the default) uses the unprefixed names.
    /// </summary>
    public string? DatabaseTablePrefix { get; set; }

    /// <summary>
    /// How often the worker polls the store for due runs and due recurring schedules.
    /// Defaults to 5 seconds. Lower values reduce execution latency at the cost of more
    /// database traffic.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>
    /// Configuration-binding alias for <see cref="PollInterval"/>, expressed in seconds.
    /// Setting a zero or negative value silently resets the interval to the 5-second
    /// default.
    /// </summary>
    public double PollIntervalSeconds
    {
        get => PollInterval.TotalSeconds;
        set => PollInterval = value > 0 ? TimeSpan.FromSeconds(value) : DefaultPollInterval;
    }

    /// <summary>
    /// Maximum number of due runs claimed from the store per poll (further capped by free
    /// execution slots under <see cref="MaxConcurrentRuns"/>). Defaults to 5. Setting a zero
    /// or negative value silently resets it to the default.
    /// </summary>
    public int ClaimBatchSize
    {
        get => _claimBatchSize;
        set => _claimBatchSize = value > 0 ? value : DefaultClaimBatchSize;
    }

    /// <summary>
    /// Legacy alias that reads and writes <see cref="ClaimBatchSize"/>; kept for
    /// configuration compatibility. Prefer <see cref="ClaimBatchSize"/>.
    /// </summary>
    public int BatchSize
    {
        get => ClaimBatchSize;
        set => ClaimBatchSize = value;
    }

    /// <summary>
    /// Maximum number of runs this worker executes concurrently; polling claims no new runs
    /// while all slots are occupied. Defaults to 5.
    /// </summary>
    public int MaxConcurrentRuns { get; set; } = DefaultMaxConcurrentRuns;

    /// <summary>
    /// How long a claimed run stays exclusively owned before other workers may reclaim it.
    /// Defaults to 30 seconds. The heartbeat runner extends the lease while a job executes,
    /// so this bounds crash detection time rather than maximum job duration.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = DefaultLeaseDuration;

    /// <summary>
    /// Configuration-binding alias for <see cref="LeaseDuration"/>, expressed in seconds.
    /// Setting a zero or negative value silently resets the duration to the 30-second
    /// default.
    /// </summary>
    public double LeaseDurationSeconds
    {
        get => LeaseDuration.TotalSeconds;
        set => LeaseDuration = value > 0 ? TimeSpan.FromSeconds(value) : DefaultLeaseDuration;
    }

    /// <summary>
    /// How long a stopping worker waits for in-flight runs to finish before cancelling them.
    /// Keep this below <c>HostOptions.ShutdownTimeout</c> or the host will abort the drain first.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = DefaultShutdownDrainTimeout;

    /// <summary>
    /// Configuration-binding alias for <see cref="ShutdownDrainTimeout"/>, expressed in
    /// seconds. Zero disables waiting; a negative value silently resets the timeout to the
    /// 10-second default.
    /// </summary>
    public double ShutdownDrainTimeoutSeconds
    {
        get => ShutdownDrainTimeout.TotalSeconds;
        set => ShutdownDrainTimeout = value >= 0 ? TimeSpan.FromSeconds(value) : DefaultShutdownDrainTimeout;
    }

    /// <summary>
    /// Base delay before retrying a failed run when the job does not specify its own initial
    /// retry delay. Defaults to 5 seconds. Under <c>RetryBehavior.Backoff</c> this base
    /// doubles with each failed attempt.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on any computed retry delay, capping exponential backoff growth. Defaults
    /// to 1 hour; a zero or negative value removes the cap.
    /// </summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// When true, each retry delay is randomized by up to ±<see cref="RetryJitterRatio"/> to
    /// spread out retry storms after a shared failure. Off by default.
    /// </summary>
    public bool RetryJitterEnabled { get; set; }

    /// <summary>
    /// Maximum fraction (0 to 1) by which a retry delay is randomly shifted up or down when
    /// <see cref="RetryJitterEnabled"/> is true. Defaults to 0.2 (±20%); values outside the
    /// range are clamped when applied.
    /// </summary>
    public double RetryJitterRatio { get; set; } = 0.2;

    /// <summary>
    /// When true (the default), each poll wait is randomized by up to
    /// <see cref="PollJitterRatio"/> so multiple workers do not hit the store in lockstep.
    /// </summary>
    public bool PollJitterEnabled { get; set; } = true;

    /// <summary>
    /// Maximum fraction (0 to 1) by which the poll interval is randomly shifted up or down
    /// when <see cref="PollJitterEnabled"/> is true. Defaults to 0.2 (±20%).
    /// </summary>
    public double PollJitterRatio { get; set; } = 0.2;

    /// <summary>
    /// Selects the non-durable in-memory store (state is lost on process exit). Returns this
    /// instance for chaining.
    /// </summary>
    public DurableStackOptions UseInMemory()
    {
        StorageProvider = DurableStackStorageProvider.InMemory;
        return this;
    }

    /// <summary>
    /// Selects PostgreSQL storage with the given connection string. Returns this instance
    /// for chaining.
    /// </summary>
    /// <param name="connectionString">Npgsql connection string for the DurableStack database.</param>
    public DurableStackOptions UsePostgres(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.Postgres;
        Postgres.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Selects SQL Server storage with the given connection string. Returns this instance
    /// for chaining.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string for the DurableStack database.</param>
    public DurableStackOptions UseSqlServer(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.SqlServer;
        SqlServer.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Selects SQLite storage with the given connection string. Returns this instance for
    /// chaining.
    /// </summary>
    /// <param name="connectionString">SQLite connection string, e.g. "Data Source=durablestack.db".</param>
    public DurableStackOptions UseSqlite(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.Sqlite;
        Sqlite.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Selects MySQL storage with the given connection string. Returns this instance for
    /// chaining.
    /// </summary>
    /// <param name="connectionString">MySQL connection string for the DurableStack database.</param>
    public DurableStackOptions UseMySql(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.MySql;
        MySql.ConnectionString = connectionString;
        return this;
    }
}
