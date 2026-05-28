using System;

namespace DurableStack.Core.Options;

public sealed class DurableStackOptions
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);

    public DurableStackStorageProvider StorageProvider { get; set; } = DurableStackStorageProvider.InMemory;

    public PostgresDurableStackOptions Postgres { get; } = new();

    public SqlServerDurableStackOptions SqlServer { get; } = new();

    public SqliteDurableStackOptions Sqlite { get; } = new();

    public MySqlDurableStackOptions MySql { get; } = new();

    public DurableStackEventingOptions Eventing { get; } = new();

    public DurableStackRecurringOptions Recurring { get; } = new();

    public DurableStackRetentionOptions Retention { get; } = new();

    public DurableStackJobRegistrationOptions JobRegistration { get; } = new();

    public string WorkerName { get; set; } = Environment.MachineName;

    public string ConnectionStringName { get; set; } = "DurableStack";

    public string? DatabaseTablePrefix { get; set; }

    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    public double PollIntervalSeconds
    {
        get => PollInterval.TotalSeconds;
        set => PollInterval = value > 0 ? TimeSpan.FromSeconds(value) : DefaultPollInterval;
    }

    public int BatchSize { get; set; } = 50;

    public TimeSpan LeaseDuration { get; set; } = DefaultLeaseDuration;

    public double LeaseDurationSeconds
    {
        get => LeaseDuration.TotalSeconds;
        set => LeaseDuration = value > 0 ? TimeSpan.FromSeconds(value) : DefaultLeaseDuration;
    }

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

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
