# Releasing

This guide defines the repeatable release process for DurableStack .NET packages.

## Scope

Packable projects:

- `DurableStack.Core`
- `DurableStack.Hosting`
- `DurableStack.Worker`
- `DurableStack.Postgres`
- `DurableStack.MySql`
- `DurableStack.SqlServer`
- `DurableStack.Sqlite`

## Version update

For release candidates:

- set `<VersionPrefix>` to `1.0.0`
- set `<VersionSuffix>` to `rc.<n>` (for example, `rc.1`)

For GA:

- keep `<VersionPrefix>` at `1.0.0`
- remove `<VersionSuffix>`

Ensure all seven packable `.csproj` files use the same version values.

## Target frameworks

Packable projects should target both:

- `net9.0`
- `net10.0`

## Validation

Run:

```bash
dotnet build DurableStack.sln
dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj
dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj -f net9.0
dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj -f net10.0
```

Additionally, run provider integration tests against real databases before stable releases:

- `DURABLESTACK_TEST_POSTGRES`
- `DURABLESTACK_TEST_MYSQL`
- `DURABLESTACK_TEST_SQLSERVER`

SQLite integration tests run by default.

## Upgrade and rollback expectations

- DurableStack migrations are additive and idempotent.
- Upgrades are forward-safe from prior prerelease installs on the same provider.
- Rollback is operational, not schema-down migration: restore the database from backup/snapshot taken before upgrade.
- For production, run migrations in deployment steps before rolling app instances.

## Pack

```bash
dotnet pack src/DurableStack.Core/DurableStack.Core.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.Hosting/DurableStack.Hosting.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.Worker/DurableStack.Worker.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.Postgres/DurableStack.Postgres.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.MySql/DurableStack.MySql.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.SqlServer/DurableStack.SqlServer.csproj -c Release -o artifacts/nuget/<version>
dotnet pack src/DurableStack.Sqlite/DurableStack.Sqlite.csproj -c Release -o artifacts/nuget/<version>
```

Optional pre-push validation (inspect package metadata and target frameworks):

```bash
dotnet nuget list source
```

## Push

```bash
dotnet nuget push "artifacts/nuget/<version>/*.nupkg" --source "https://api.nuget.org/v3/index.json" --skip-duplicate
```

If wildcard expansion is unsupported in your shell, loop over files and push each path explicitly.

## Notes

- CI does not auto-publish packages.
- Symbol packages (`.snupkg`) may report duplicate conflicts if pushed more than once.
- Keep docs and examples aligned with any API or behavior changes included in the release.
