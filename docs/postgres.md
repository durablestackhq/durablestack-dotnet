# PostgreSQL Provider

DurableStack supports PostgreSQL as an opt-in durable storage backend.

## Configuration

Use `ConnectionStrings:DurableStack` and set provider mode under `DurableStack`.

```json
{
  "ConnectionStrings": {
    "DurableStack": "Host=localhost;Port=5432;Database=durable_stack;Username=postgres;Password=postgres"
  },
  "DurableStack": {
    "StorageProvider": "Postgres",
    "DatabaseTablePrefix": "Acme_"
  }
}
```

Then initialize:

```csharp
builder.Services.AddDurableStackPostgres(builder.Configuration, options =>
{
    options.WorkerName = $"{Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName}-{Environment.ProcessId}";
    options.PollInterval = TimeSpan.FromMilliseconds(500);
    options.BatchSize = 25;
    options.LeaseDuration = TimeSpan.FromSeconds(30);
});
```

## Table naming and prefixes

Default table names:

- `durable_stack_jobs`
- `durable_stack_job_runs`
- `durable_stack_job_locks`

When `DatabaseTablePrefix` is set, PostgreSQL provider lowercases it and prepends it to all tables.

Example:

- `DatabaseTablePrefix = "Acme_"`

Resolves to:

- `acme_durable_stack_jobs`
- `acme_durable_stack_job_runs`
- `acme_durable_stack_job_locks`

Prefix validation (PostgreSQL provider):

- letters, digits, and underscores only

## Migrations

DurableStack now auto-runs PostgreSQL migration setup at worker startup.

On startup, it ensures required tables and indexes exist.

This is idempotent and safe to run on multiple instances.

## Claiming and distributed execution

Due work is claimed using `FOR UPDATE SKIP LOCKED` in a transaction.

This enables multiple workers to poll the same database while ensuring each run row is claimed by only one worker at a time.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Required database capabilities

The configured PostgreSQL user must be able to:

- create tables
- create indexes
- insert/update/select/delete in DurableStack tables

For production environments with restricted roles, run migrations separately with elevated privileges, then run workers with DML-only permissions.

## Integration tests

PostgreSQL integration tests are included in `src/DurableStack.Tests/PostgresIntegrationTests.cs`.

To run them against a live PostgreSQL instance, set:

- `DURABLESTACK_TEST_POSTGRES=<connection-string>`

Without this environment variable, tests are skipped.
