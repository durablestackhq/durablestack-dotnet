# MySQL Provider

DurableStack supports MySQL as an opt-in durable storage backend.

## Configuration

Use `ConnectionStrings:DurableStack` and set provider mode under `DurableStack`.

```json
{
  "ConnectionStrings": {
    "DurableStack": "Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;SslMode=Preferred"
  },
  "DurableStack": {
    "StorageProvider": "MySql",
    "DatabaseTablePrefix": "Acme_"
  }
}
```

Then initialize:

```csharp
builder.Services.AddDurableStackMySql(builder.Configuration, options =>
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

## Claiming and distributed execution

Due work is claimed atomically using row locks with `FOR UPDATE SKIP LOCKED`.

This enables multiple workers to poll the same database while ensuring each run row is claimed by only one worker at a time.

Leased runs are reclaimed when `lease_until_utc` expires, and long-running jobs extend leases periodically while executing.

For best results, configure unique worker identities per process/container (`DurableStackOptions.WorkerName`) so lease ownership and heartbeat extension stay isolated per instance.

## Integration tests

MySQL integration tests are included in `src/DurableStack.Tests/MySqlIntegrationTests.cs`.

To run them against a live MySQL instance, set:

- `DURABLESTACK_TEST_MYSQL=<connection-string>`

Without this environment variable, tests are skipped.
