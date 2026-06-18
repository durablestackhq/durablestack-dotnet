# SQL Server Provider

DurableStack supports SQL Server as an opt-in durable storage backend.

## Configuration

Set provider mode under `DurableStack` and provide `DurableStack:SqlServer:ConnectionString`.

```json
{
  "DurableStack": {
    "StorageProvider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true"
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
builder.Services.AddDurableStackSqlServer(builder.Configuration, options =>
{
    options.WorkerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
});
```

If these tuning values are omitted, defaults are `PollInterval=5s`, `BatchSize=5`, and `LeaseDuration=30s`.

Poll jitter is available for multi-worker deployments:

- `PollJitterEnabled=false` by default
- `PollJitterRatio=0.2` by default (used when jitter is enabled)

If your app uses a non-default connection string name, set `options.ConnectionStringName`.

## Connection string and TLS guidance

Recommended production defaults:

- `Encrypt=True`
- `TrustServerCertificate=False`
- explicit server/instance and database
- credentials from secret stores or managed identity, not checked-in files

Example (SQL authentication):

```text
Server=tcp:sql-prod.example.net,1433;Database=durable_stack;User Id=durablestack_app;Password=<secret>;Encrypt=True;TrustServerCertificate=False;
```

For local/dev containers or self-signed cert environments, `TrustServerCertificate=True` is acceptable temporarily, but avoid carrying that setting to production.

## Table naming and prefixes

Default table names:

- `durable_stack_jobs`
- `durable_stack_job_runs`
- `durable_stack_job_locks`

When `DatabaseTablePrefix` is set, SQL Server preserves the caller-provided casing and prepends it to all tables.

Example:

- `DatabaseTablePrefix = "Acme_"`

Resolves to:

- `Acme_durable_stack_jobs`
- `Acme_durable_stack_job_runs`
- `Acme_durable_stack_job_locks`

Prefix validation (SQL Server provider):

- letters, digits, and underscores only
- max length: 16 characters

## Migrations

DurableStack auto-runs SQL Server migration setup at worker startup.

On startup, it ensures required tables and indexes exist, including recurring slot uniqueness and lease/due indexes.

This is idempotent and safe to run on multiple instances.

Rollback expectation: if an upgrade must be reverted, restore from a pre-upgrade database backup/snapshot.

### Production migration strategy

If your runtime identity has restricted permissions, use a split-role approach:

1. Run migrations with an elevated deployment identity (DDL allowed).
2. Run worker/API processes with a least-privilege app identity (DML only).

You can trigger migrations programmatically via `IDurableStackStoreMigrator`, or run a dedicated migration step in deployment before starting application instances.

## Claiming and distributed execution

Due work is claimed atomically using SQL Server locking hints (`UPDLOCK`, `READPAST`, `ROWLOCK`) in the claim update path.

This enables multiple workers to poll the same database while ensuring each run row is claimed by only one worker at a time.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Query behavior

SQL Server provider supports bounded store-side queries for:

- recent runs
- runs by status
- runs by job name
- enqueue-only runs (`schedule_slot_utc is null`)

## Required database capabilities

Migration/deployment identity (DDL + DML) should be able to:

- create tables
- create indexes
- insert/update/select/delete in DurableStack tables

Runtime app identity (DML only) should be able to:

- select/insert/update/delete in DurableStack tables

In many teams this maps to a dedicated schema owner/deployer role and a separate application role.

## Security notes for example endpoints

The example app includes a `/migrate` endpoint for convenience.

- keep this endpoint disabled or protected in production
- prefer running migrations in deployment pipelines instead of exposing migration triggers publicly

## Integration tests

SQL Server integration tests are included in `src/DurableStack.Tests/SqlServerIntegrationTests.cs`.

To run them against a live SQL Server instance, set:

- `DURABLESTACK_TEST_SQLSERVER=<connection-string>`

Without this environment variable, tests are skipped.
