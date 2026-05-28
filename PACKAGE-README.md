# DurableStack (Prerelease)

Early prerelease package for DurableStack. API may change before 1.0.

DurableStack provides durable background and scheduled jobs backed by relational databases.

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
- Docs: https://github.com/durablestackhq/durablestack-dotnet/tree/main/docs
