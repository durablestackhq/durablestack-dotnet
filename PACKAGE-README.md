# DurableStack (Prerelease)

Early prerelease package for DurableStack. API may change before 1.0.

DurableStack provides durable background and scheduled jobs backed by relational databases.

## Package selection

- `DurableStack.Core`: core runtime + in-memory support (no extra provider package required for in-memory)
- `DurableStack.Hosting`: generic hosting + dependency-injection integration and provider wiring
- `DurableStack.Worker`: worker-service hosting integration
- Provider packages: `DurableStack.Postgres`, `DurableStack.MySql`, `DurableStack.SqlServer`, `DurableStack.Sqlite`

## Repository and docs

- Repository: https://github.com/durablestackhq/durablestack-dotnet
- Docs: https://github.com/durablestackhq/durablestack-dotnet/tree/main/docs
