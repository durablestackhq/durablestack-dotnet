# DurableStack (.NET)

DurableStack is a database-backed background job runtime for .NET applications.

## Prerelease notice

This repository ships the first .NET prerelease line (`0.0.1-alpha.*`).

- API may change before `1.0.0`.
- Schema and runtime behavior may evolve during alpha.

## What works today

- Durable one-off and delayed jobs.
- Recurring cron jobs with IANA timezone support.
- Distributed-safe claiming, leasing, lease expiry reclaim, and heartbeat extension.
- Retry and terminal failure transitions.
- Startup-safe migrations for relational providers.
- Query APIs for recent and status-filtered runs.
- Event sink abstraction with logging and API ingestion support.
- OpenTelemetry hooks.

Supported providers in this .NET implementation:

- InMemory
- PostgreSQL
- MySQL
- SQL Server
- SQLite

## Runtime roadmap

DurableStack is planned as a multi-runtime family with shared database contracts:

- `durablestack-dotnet` (this repo)
- `durablestack-typescript` (planned)
- `durablestack-python` (planned)

Important: we do not currently promise fully interoperable cross-language worker execution. The goal is consistent contracts first, then broader interoperability over time.

## Observability roadmap

Planned DurableStack observability offering:

- Free hosted observability tier.
- Paid tiers with longer retention, alerting, and expanded operational features.

This roadmap is directional and may evolve during prerelease.

## Packages

Published package IDs:

- `DurableStack.Core`
- `DurableStack.Postgres`
- `DurableStack.MySql`
- `DurableStack.SqlServer`
- `DurableStack.Sqlite`
- `DurableStack.Hosting`
- `DurableStack.Worker`

## Docs

- `docs/getting-started.md`
- `docs/providers.md`
- `docs/postgres.md`
- `docs/mysql.md`
- `docs/sqlserver.md`
- `docs/sqlite.md`
- `docs/reliability-model.md`
- `docs/timezones.md`
- `docs/events.md`
- `docs/job-registration.md`

## Local development

- Build: `dotnet build DurableStack.sln`
- Test: `dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj`

## Installation guidance

| Scenario | Install | Includes |
| --- | --- | --- |
| Default quick start | `DurableStack.Hosting` | Hosting/DI integration + `Core` + relational providers (`Postgres`, `MySql`, `SqlServer`, `Sqlite`) |
| Worker-host quick start | `DurableStack.Worker` | Worker-host helper APIs + `DurableStack.Hosting` dependencies |
| Minimal/custom | `DurableStack.Core` + one provider package | Smallest dependency set for targeted deployments |
| In-memory only | `DurableStack.Core` | In-memory provider support is built in |

Recommended default for most apps: start with `DurableStack.Hosting`, then move to `DurableStack.Core` + a single provider if you want a leaner dependency footprint.

## Job setup

- Default behavior: `AddDurableStack(...)` auto-discovers public `IDurableJob` and `IDurableJob<TArgs>` classes in the app assembly
- Recurring jobs: add `[RecurringJob("...")]` on the class (optional `[DurableJob]` can override name/max attempts)
- Power-user mode: set `options.JobRegistration.AutoDiscoverJobsFromAssembly = false` and use explicit `AddDurableJob<...>(...)`

## Project metadata

- License: `LICENSE` (MIT)
- Contribution guide: `CONTRIBUTING.md`
- Security policy: `SECURITY.md`
