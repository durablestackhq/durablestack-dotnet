# SQLite Provider

DurableStack supports SQLite as an opt-in durable storage backend.

## Configuration

Use `ConnectionStrings:DurableStack` and set provider mode under `DurableStack`.

```json
{
  "ConnectionStrings": {
    "DurableStack": "Data Source=durable_stack.db"
  },
  "DurableStack": {
    "StorageProvider": "Sqlite",
    "DatabaseTablePrefix": "Acme_"
  }
}
```

Then initialize:

```csharp
builder.Services.AddDurableStackSqlite(builder.Configuration, options =>
{
    options.WorkerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

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

## Claiming and distributed execution

Due work is claimed atomically via transactional select-and-update semantics.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Integration tests

SQLite integration tests are included in `src/DurableStack.Tests/SqliteIntegrationTests.cs`.

These tests run by default using isolated temporary SQLite files.
