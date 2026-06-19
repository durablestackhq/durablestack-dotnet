# DurableStack (.NET)

Run durable background jobs in .NET using the database you already have.

DurableStack provides recurring scheduling, retries, distributed execution, and worker observability without requiring Redis, RabbitMQ, or additional queue infrastructure.

## Prerelease notice

This repository ships the first .NET beta line (`0.1.*`).

- API may change before `1.0.0`.
- Schema and runtime behavior may evolve during beta.

## What works today

- Durable one-off and delayed jobs.
- Recurring cron jobs with IANA timezone support (5-field and 6-field with seconds).
- Startup registration sync policies (`KeepDatabase`/`UpdateFromCode`, `Disable`/`Ignore`).
- Recurring jobs can be declared disabled at registration (`Enabled = false`).
- Distributed-safe claiming, leasing, lease expiry reclaim, and heartbeat extension.
- Retry and terminal failure transitions.
- Poll jitter controls for multi-worker fairness (`PollJitterEnabled`, `PollJitterRatio`).
- Discoverable jitter helper: `AddDurableStackWithJitter(...)`.
- Optional, hosted observability via https://app.durablestack.com
- Startup-safe migrations for relational providers.
- Query APIs for recent and status-filtered runs.
- Event sink abstraction with logging and API ingestion support.
- OpenTelemetry hooks with automated instrumentation coverage.

Supported providers in this .NET implementation:

- InMemory
- PostgreSQL
- MySQL
- SQL Server
- SQLite

## Feature comparison

| Feature               | DurableStack | Hangfire | Quartz |
| --------------------- | ------------ | -------- | ------ |
| Database-backed       | Yes          | Yes      | Yes    |
| Distributed execution | Yes          | Yes      | Yes    |
| OpenTelemetry         | Yes          | Partial  | Varies |
| Hosted observability  | Yes          | No       | No     |
| Uses existing DB      | Yes          | Yes      | Yes    |
| Multi-runtime roadmap | Yes          | No       | No     |

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

- Live docs: https://docs.durablestack.com/docs/latest/dotnet
- Quickstart: https://docs.durablestack.com/docs/latest/dotnet/quickstart
- Getting started: https://docs.durablestack.com/docs/latest/dotnet/getting-started
- Providers: https://docs.durablestack.com/docs/latest/dotnet/providers
- `docs/getting-started.md`
- `docs/providers.md`
- `docs/postgres.md`
- `docs/mysql.md`
- `docs/sqlserver.md`
- `docs/sqlite.md`
- `docs/reliability-model.md`
- `docs/timezones.md`
- `docs/events.md`
- `docs/opentelemetry.md`
- `docs/job-registration.md`
- `docs/scheduled-job-management.md`
- `docs/data-retention.md`
- `docs/api-stability.md`
- `docs/releasing.md`

## Local development

- Build: `dotnet build DurableStack.sln`
- Test: `dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj`

## CI

DurableStack uses CI for restore/build/test validation on pushes and pull requests.

- Workflow: `.github/workflows/ci.yml`
- CI does not publish packages to NuGet.

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
- Recurring jobs: add `[RecurringJob("...")]` on the class (5-field or 6-field cron; optional `[DurableJob]` can override name/max attempts)
- Disabled at startup: use `[RecurringJob(..., Enabled = false)]` when needed
- Power-user mode: set `options.JobRegistration.AutoDiscoverJobsFromAssembly = false` and use explicit `AddDurableJob<...>(...)`
- Startup sync behavior defaults: `ExistingJobBehavior=KeepDatabase`, `OrphanedJobBehavior=Disable`
- Non-hosted/manual loops: call `provider.InitializeDurableStackAsync(...)` once; hosted apps already initialize automatically
- Multi-worker helper: `AddDurableStackWithJitter(0.2, ...)` enables poll jitter for better workload distribution

## Project metadata

- License: `LICENSE` (MIT)
- Contribution guide: `CONTRIBUTING.md`
- Security policy: `SECURITY.md`

## Upcoming changes

Planned hardening items toward `1.0.0`:

- eventing hardening for high-throughput and slow-sink resilience
- expanded provider integration checks in CI
- continued docs-site alignment and operational runbook polish

For stability guarantees and release process details, see `docs/api-stability.md` and `docs/releasing.md`.
