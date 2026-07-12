using System;

namespace DurableStack.Core.Options;

public sealed class DurableStackOptions
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultShutdownDrainTimeout = TimeSpan.FromSeconds(10);
    private const int DefaultClaimBatchSize = 5;
    private const int DefaultMaxConcurrentRuns = 5;
    private int _claimBatchSize = DefaultClaimBatchSize;

    public static string CreateDefaultWorkerName()
    {
        var host = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        return $"{host}-{Environment.ProcessId}";
    }

    public DurableStackStorageProvider StorageProvider { get; set; } = DurableStackStorageProvider.InMemory;

    public PostgresDurableStackOptions Postgres { get; } = new();

    public SqlServerDurableStackOptions SqlServer { get; } = new();

    public SqliteDurableStackOptions Sqlite { get; } = new();

    public MySqlDurableStackOptions MySql { get; } = new();

    public DurableStackEventingOptions Eventing { get; } = new();

    public DurableStackRecurringOptions Recurring { get; } = new();

    public DurableStackRetentionOptions Retention { get; } = new();

    public DurableStackJobRegistrationOptions JobRegistration { get; } = new();

    public DurableStackJobActivationMode JobActivation { get; set; } = DurableStackJobActivationMode.ScopedPerExecution;

    public string WorkerName { get; set; } = CreateDefaultWorkerName();

    public string? DatabaseTablePrefix { get; set; }

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    public double PollIntervalSeconds
    {
        get => PollInterval.TotalSeconds;
        set => PollInterval = value > 0 ? TimeSpan.FromSeconds(value) : DefaultPollInterval;
    }

    public int ClaimBatchSize
    {
        get => _claimBatchSize;
        set => _claimBatchSize = value > 0 ? value : DefaultClaimBatchSize;
    }

    public int BatchSize
    {
        get => ClaimBatchSize;
        set => ClaimBatchSize = value;
    }

    public int MaxConcurrentRuns { get; set; } = DefaultMaxConcurrentRuns;

    public TimeSpan LeaseDuration { get; set; } = DefaultLeaseDuration;

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

    public double ShutdownDrainTimeoutSeconds
    {
        get => ShutdownDrainTimeout.TotalSeconds;
        set => ShutdownDrainTimeout = value >= 0 ? TimeSpan.FromSeconds(value) : DefaultShutdownDrainTimeout;
    }

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    public bool RetryJitterEnabled { get; set; }

    public double RetryJitterRatio { get; set; } = 0.2;

    public bool PollJitterEnabled { get; set; } = true;

    public double PollJitterRatio { get; set; } = 0.2;

    public DurableStackOptions UseInMemory()
    {
        StorageProvider = DurableStackStorageProvider.InMemory;
        return this;
    }

    public DurableStackOptions UsePostgres(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.Postgres;
        Postgres.ConnectionString = connectionString;
        return this;
    }

    public DurableStackOptions UseSqlServer(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.SqlServer;
        SqlServer.ConnectionString = connectionString;
        return this;
    }

    public DurableStackOptions UseSqlite(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.Sqlite;
        Sqlite.ConnectionString = connectionString;
        return this;
    }

    public DurableStackOptions UseMySql(string connectionString)
    {
        StorageProvider = DurableStackStorageProvider.MySql;
        MySql.ConnectionString = connectionString;
        return this;
    }
}
