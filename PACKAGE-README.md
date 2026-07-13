# DurableStack

DurableStack provides durable background and scheduled jobs backed by relational databases.

Supported target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Package selection

- Default quick start: `DurableStack.Hosting` (bundle-style package that brings hosting/DI + relational provider packages)
- Worker-host quick start: `DurableStack.Worker`
- Minimal/custom: `DurableStack.Core` + one provider package
- In-memory only: `DurableStack.Core` (in-memory support is built in)

Provider packages: `DurableStack.Postgres`, `DurableStack.MySql`, `DurableStack.SqlServer`, `DurableStack.Sqlite`.

## Reliability defaults

- Lease-based claiming with heartbeat lease extension for active runs
- Lease-fenced completion writes (stale workers cannot overwrite the current owner)
- Retry scheduling with terminal failure when attempts are exhausted
- Graceful shutdown drain window for in-flight runs

## Eventing and security defaults

- Hosted ingestion enables automatically when both `Eventing.TenantId` and `Eventing.ClientSecret` are configured
- `Eventing.IncludeErrorDetail` defaults to `false` (exception type is emitted, message/stack detail is redacted)
- `Eventing.IngestionApiBaseUrl` must be absolute; non-loopback endpoints must use `https`

## Job registration defaults

- `AddDurableStack(...)` auto-discovers public `IDurableJob` and `IDurableJob<TArgs>` classes from the app assembly
- Default job name: class name
- Default max attempts: `3`
- Add `[RecurringJob("...")]` to schedule recurring jobs
- Without `[RecurringJob]`, jobs are enqueue-only

## Repository and docs

- Repository: https://github.com/durablestackhq/durablestack-dotnet
- Docs: https://docs.durablestack.com/docs/latest/dotnet
- Quickstart: https://docs.durablestack.com/docs/latest/dotnet/quickstart
