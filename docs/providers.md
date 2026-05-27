# Provider Contract

This document defines the expected behavior for all DurableStack storage providers.

It is the parity contract that all current and future DurableStack providers should follow.

## Core responsibilities

A provider implementation must satisfy `IDurableJobStore` and, when supported, `IDurableStackStoreMigrator`.

Required operations:

- enqueue one-off and delayed runs
- claim due runs safely for distributed workers
- mark succeeded
- mark failed (retry + terminal failure semantics)
- query run state
- migration/init support for durable providers

## Execution semantics

Providers must preserve these runtime semantics:

- claim only due `pending` runs
- reclaim `leased` runs when lease expiry is reached
- increment attempt on successful claim
- set lease ownership on claim
- extend lease for in-flight long-running work (heartbeat)
- clear lease ownership on terminal transition (`succeeded`/`failed`)
- support retry by moving run back to `pending` with updated schedule

Expected guarantee:

- effectively-once execution in normal operation
- duplicate prevention via atomic claim path

## Distributed claim behavior

Providers must implement an atomic claim strategy safe for multiple workers.

Equivalent outcomes to established row-claim patterns (for example, row locking with skip semantics or atomic claim updates) are required.

## Table naming and prefix contract

Base table names:

- `durable_stack_jobs`
- `durable_stack_job_runs`
- `durable_stack_job_locks`

If `DatabaseTablePrefix` is set, providers prepend it to all table names.

Case handling is provider-specific and should be documented per provider.

Current implementations:

- PostgreSQL: prefix is normalized to lowercase
- MySQL: prefix preserves caller casing
- SQL Server: prefix preserves caller casing
- SQLite: prefix preserves caller casing

Prefix validation:

- providers should accept letters, digits, and underscores
- providers should reject unsupported characters with clear errors

## Migration behavior

Durable providers should implement startup-safe, idempotent migration/init behavior.

Requirements:

- safe to run repeatedly
- safe when multiple app instances start concurrently
- create required tables/indexes when missing

In-memory provider:

- migration is a no-op

## Query behavior

Providers should return run data mapped consistently across stores:

- UTC timestamps
- status values (`pending`, `leased`, `succeeded`, `failed`)
- attempt/max attempts
- lease owner/lease expiry
- error message payloads

## Recurring job timezone contract

Recurring job schedules must use IANA time zone IDs.

Examples:

- `UTC`
- `America/Chicago`
- `Europe/London`

Windows time zone IDs are not accepted for recurring schedules.

Examples of rejected values:

- `Central Standard Time`
- `GMT Standard Time`

This keeps schedule definitions portable across .NET, Node.js, and other runtimes.

Recurring catch-up behavior is controlled by `DurableStackOptions.Recurring.CatchUpPolicy`.

- `SkipMissed` (default): materialize the current due slot, then advance to the next future slot
- `CatchUp`: materialize one missed slot per loop until caught up

Example configuration:

```csharp
using DurableStack.Core.Options;

builder.Services.AddDurableStack(options =>
{
    options.Recurring.CatchUpPolicy = RecurringCatchUpPolicy.SkipMissed;
});
```

## Error handling guidance

Providers should:

- throw clear configuration errors at startup when required connection settings are missing
- surface transient DB failures to caller (for runtime retry handling at higher layers)
- avoid silent fallbacks that hide misconfiguration

## Provider readiness checklist

Before promoting a provider beyond preview, verify:

- claim path is atomic under parallel workers
- retry transitions behave as expected
- prefix behavior matches contract
- migrations are idempotent and concurrency-safe
- integration tests pass against a live database instance

Current provider docs:

- `docs/postgres.md`
- `docs/mysql.md`
- `docs/sqlserver.md`
- `docs/sqlite.md`
