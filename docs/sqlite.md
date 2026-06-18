# SQLite Provider

DurableStack supports SQLite as an opt-in durable storage backend.

## Configuration

Set provider mode under `DurableStack` and provide `DurableStack:Sqlite:ConnectionString`.

```json
{
  "DurableStack": {
    "StorageProvider": "Sqlite",
    "Sqlite": {
      "ConnectionString": "Data Source=durable_stack.db"
    },
    "DatabaseTablePrefix": "Acme_",
    "PollIntervalSeconds": 0.5,
    "BatchSize": 25,
    "LeaseDurationSeconds": 5
  }
}
```

Then initialize:

```csharp
builder.Services.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
});
```

If these tuning values are omitted, defaults are `PollInterval=5s`, `BatchSize=5`, and `LeaseDuration=30s`.

Poll jitter is available for multi-worker deployments:

- `PollJitterEnabled=false` by default
- `PollJitterRatio=0.2` by default (used when jitter is enabled)

If your app uses a non-default connection string name, set `options.ConnectionStringName`.

## Connection string guidance

Common patterns:

- file-backed database: `Data Source=durable_stack.db`
- absolute path: `Data Source=C:\data\durable_stack.db`

For production, use file paths on persistent volumes and include backup/restore strategy in operations runbooks.

## Table naming and prefixes

Default table names:

- `durable_stack_jobs`
- `durable_stack_job_runs`
- `durable_stack_job_locks`

When `DatabaseTablePrefix` is set, SQLite preserves the caller-provided casing and prepends it to all tables.

Example:

- `DatabaseTablePrefix = "Acme_"`

Resolves to:

- `Acme_durable_stack_jobs`
- `Acme_durable_stack_job_runs`
- `Acme_durable_stack_job_locks`

Prefix validation (SQLite provider):

- letters, digits, and underscores only
- max length: 16 characters

## Migrations

DurableStack auto-runs SQLite migration setup at worker startup.

On startup, it ensures required tables and indexes exist, including recurring slot uniqueness and lease/due indexes.

This is idempotent and safe to run repeatedly.

Rollback expectation: if an upgrade must be reverted, restore from a pre-upgrade database backup or file snapshot.

## Claiming and distributed execution

Due work is claimed atomically via transactional select-and-update semantics.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Query behavior

SQLite provider supports bounded store-side queries for:

- recent runs
- runs by status
- runs by job name
- enqueue-only runs (`schedule_slot_utc is null`)

## Integration tests

SQLite integration tests are included in `src/DurableStack.Tests/SqliteIntegrationTests.cs`.

These tests run by default using isolated temporary SQLite files.
