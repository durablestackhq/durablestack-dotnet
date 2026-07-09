# DurableStack

Stable package for DurableStack.

DurableStack provides durable background and scheduled jobs backed by relational databases.

Supported target frameworks: `net9.0`, `net10.0`.

## Package selection

- Default quick start: `DurableStack.Hosting` (bundle-style package that brings hosting/DI + relational provider packages)
- Worker-host quick start: `DurableStack.Worker`
- Minimal/custom: `DurableStack.Core` + one provider package
- In-memory only: `DurableStack.Core` (in-memory support is built in)

Provider packages: `DurableStack.Postgres`, `DurableStack.MySql`, `DurableStack.SqlServer`, `DurableStack.Sqlite`.

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
