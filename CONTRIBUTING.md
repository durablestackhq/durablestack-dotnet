# Contributing

Thanks for contributing to DurableStack.

## Prerequisites

- .NET SDK 9.0+
- Optional local databases for integration tests:
  - PostgreSQL
  - MySQL
  - SQL Server

## Build and test

```powershell
dotnet restore DurableStack.sln
dotnet build DurableStack.sln
dotnet test src/DurableStack.Tests/DurableStack.Tests.csproj
```

Integration tests for relational providers are environment-variable gated:

- `DURABLESTACK_TEST_POSTGRES`
- `DURABLESTACK_TEST_MYSQL`
- `DURABLESTACK_TEST_SQLSERVER`

If unset, those integration tests are skipped.

## Pull requests

- Keep PRs focused and small where possible.
- Include tests for behavior changes.
- Update docs when public behavior or configuration changes.
