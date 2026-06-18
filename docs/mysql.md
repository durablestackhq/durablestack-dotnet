# MySQL Provider

DurableStack supports MySQL as an opt-in durable storage backend.

## Configuration

Set provider mode under `DurableStack` and provide `DurableStack:MySql:ConnectionString`.

```json
{
  "DurableStack": {
    "StorageProvider": "MySql",
    "MySql": {
      "ConnectionString": "Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;SslMode=Preferred"
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
builder.Services.AddDurableStackMySql(builder.Configuration, options =>
{
    options.WorkerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
});
```

If these tuning values are omitted, defaults are `PollInterval=5s`, `BatchSize=5`, and `LeaseDuration=30s`.

Poll jitter is available for multi-worker deployments:

- `PollJitterEnabled=false` by default
- `PollJitterRatio=0.2` by default (used when jitter is enabled)

If your app uses a non-default connection string name, set `options.ConnectionStringName`.

## Table naming and prefixes

Default table names:

- `durable_stack_jobs`
- `durable_stack_job_runs`
- `durable_stack_job_locks`

When `DatabaseTablePrefix` is set, MySQL preserves the caller-provided casing and prepends it to all tables.

Example:

- `DatabaseTablePrefix = "Acme_"`

Resolves to:

- `Acme_durable_stack_jobs`
- `Acme_durable_stack_job_runs`
- `Acme_durable_stack_job_locks`

Prefix validation (MySQL provider):

- letters, digits, and underscores only
- max length: 16 characters

## Migrations

DurableStack auto-runs MySQL migration setup at worker startup.

On startup, it ensures required tables and indexes exist, including recurring slot uniqueness and lease/due indexes.

This is idempotent and safe to run repeatedly.

Rollback expectation: if an upgrade must be reverted, restore from a pre-upgrade database backup/snapshot.

## Claiming and distributed execution

Due work is claimed atomically using row locks with `FOR UPDATE SKIP LOCKED`.

This enables multiple workers to poll the same database while ensuring each run row is claimed by only one worker at a time.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Query behavior

MySQL provider supports bounded store-side queries for:

- recent runs
- runs by status
- runs by job name
- enqueue-only runs (`schedule_slot_utc is null`)

## Integration tests

MySQL integration tests are included in `src/DurableStack.Tests/MySqlIntegrationTests.cs`.

To run them against a live MySQL instance, set:

- `DURABLESTACK_TEST_MYSQL=<connection-string>`

Without this environment variable, tests are skipped.
