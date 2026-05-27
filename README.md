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
- `DurableStack.AspNetCore`
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

## Local development

- Build: `dotnet build DurableStack.sln`
- Test: `dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj`

## Pre-release pack and publish (PowerShell)

Pack with explicit version:

```powershell
$version = "0.0.1-alpha.1"
$outDir = ".\artifacts\packages"

dotnet pack .\src\DurableStack.Core\DurableStack.Core.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.Postgres\DurableStack.Postgres.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.MySql\DurableStack.MySql.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.SqlServer\DurableStack.SqlServer.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.Sqlite\DurableStack.Sqlite.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.AspNetCore\DurableStack.AspNetCore.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
dotnet pack .\src\DurableStack.Worker\DurableStack.Worker.csproj -c Release -o $outDir --include-symbols --include-source -p:Version=$version
```

Push to NuGet (`NUGET_API_KEY` required):

```powershell
dotnet nuget push .\artifacts\packages\*.nupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
dotnet nuget push .\artifacts\packages\*.snupkg --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY --skip-duplicate
```

## Project metadata

- License: `LICENSE` (MIT)
- Contribution guide: `CONTRIBUTING.md`
- Security policy: `SECURITY.md`
