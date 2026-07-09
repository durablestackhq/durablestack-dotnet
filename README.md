# DurableStack (.NET)

**Run durable background jobs in .NET using the database you already have.**

DurableStack provides recurring scheduling, retries, distributed execution, and worker observability **without requiring Redis, RabbitMQ, or additional queue infrastructure**.

[![GitHub stars](https://img.shields.io/github/stars/durablestackhq/durablestack-dotnet)](https://github.com/durablestackhq/durablestack-dotnet)
[![NuGet](https://img.shields.io/nuget/v/DurableStack.Hosting)](https://www.nuget.org/profiles/DurableStack)

## Why DurableStack?

- **Database-native execution** — Use your existing SQL Server, PostgreSQL, MySQL, or SQLite as the coordination layer.
- **Distributed-safe by default** — Lease-based claiming, heartbeats, and safe reclaim on failure.
- **Production observability** — OpenTelemetry integration + optional hosted dashboards at [app.durablestack.com](https://app.durablestack.com).
- **Multi-runtime roadmap** — .NET today, TypeScript & Python coming soon with consistent contracts.

## Quick Start

```bash
dotnet add package DurableStack.Hosting
```

```csharp
services.AddDurableStack(options =>
{
    options.UsePostgreSql(connectionString);
    // Optional: Connect to hosted observability
    // options.TenantId = "...";
});
```

See the [full Quickstart](https://docs.durablestack.com/docs/latest/dotnet/quickstart) in the documentation.

## Key Features

- Durable one-off, delayed, and recurring (cron) jobs with timezone support.
- Retry policies, terminal failure handling, and distributed worker coordination.
- Multi-provider support (PostgreSQL, MySQL, SQL Server, SQLite, InMemory).
- OpenTelemetry + custom event sinks.
- Hosted observability platform (free tier available).

## Comparison

| Feature                  | DurableStack       | Hangfire     | Quartz.NET   |
|--------------------------|--------------------|--------------|--------------|
| Database-backed          | Yes                | Yes          | Yes          |
| Distributed execution    | Yes (lease-based)  | Yes          | Yes          |
| OpenTelemetry            | First-class        | Partial      | Varies       |
| Hosted observability     | Yes (optional)     | No           | No           |
| Multi-runtime roadmap    | Yes                | No           | No           |

Full comparison → [durablestack.com/alternatives](https://durablestack.com/alternatives)

## Getting Started

- [Documentation](https://docs.durablestack.com)
- [App / Hosted Observability](https://app.durablestack.com)
- [NuGet Packages](https://www.nuget.org/profiles/DurableStack)

## Status

v1.0.1 is now available! This marks our first stable 1.0 release. API is expected to stabilize further with community feedback.

## Community & Support

- [GitHub Discussions](https://github.com/durablestackhq/durablestack-dotnet/discussions) — Ask questions and suggest features.
- [r/DurableStack](https://www.reddit.com/r/DurableStack/)
- Built in public — follow along and contribute!

---

**License**: MIT
**Contributing**: See [CONTRIBUTING.md](CONTRIBUTING.md)
