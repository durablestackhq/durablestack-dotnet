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

Update `<VersionSuffix>` in all seven packable `.csproj` files.

## Validation

Run:

```bash
dotnet build DurableStack.sln
dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj
```

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

## Push

```bash
dotnet nuget push "artifacts/nuget/<version>/*.nupkg" --source "https://api.nuget.org/v3/index.json" --skip-duplicate
```

If wildcard expansion is unsupported in your shell, loop over files and push each path explicitly.

## Notes

- CI does not auto-publish packages.
- Symbol packages (`.snupkg`) may report duplicate conflicts if pushed more than once.
- Keep docs and examples aligned with any API or behavior changes included in the release.
