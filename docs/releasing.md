# Releasing

DurableStack packages are published to NuGet by the automated release workflow
(`.github/workflows/release.yml`). Releases are driven by git tags: **the tag
decides the version**, and nothing is packed on a developer machine.

## Scope

Packable projects (all published together, always with the same version):

- `DurableStack.Core`
- `DurableStack.Hosting`
- `DurableStack.Worker`
- `DurableStack.Postgres`
- `DurableStack.MySql`
- `DurableStack.SqlServer`
- `DurableStack.Sqlite`

## How to release

1. Merge the release content to `main` and confirm CI is green.
2. Create a tag named `v<version>` on the commit you want to ship and push it:

   - Stable: `v1.1.0` publishes version `1.1.0`.
   - Release candidate: `v1.1.0-rc.1` publishes the `1.1.0-rc.1` prerelease
     (any `-suffix` on the tag produces a NuGet prerelease and marks the
     GitHub release as a prerelease).

3. The release workflow then, in order:

   - validates the tag format (`vMAJOR.MINOR.PATCH[-prerelease]`);
   - builds the solution with `-p:Version=<version>` and
     `-p:ContinuousIntegrationBuild=true` (deterministic build);
   - runs the full test suite on net8.0, net9.0, and net10.0 against real
     PostgreSQL, MySQL, and SQL Server service containers — **a test failure
     aborts the release before anything is published**;
   - packs all seven packages and pushes them (with symbol packages) to
     NuGet.org using the `NUGET_API_KEY` repository secret;
   - attaches the packages to the workflow run and creates a GitHub release
     with generated notes.

There is nothing to edit in any `.csproj`: the `<VersionPrefix>` in
`src/Directory.Build.props` is only a fallback for local developer builds and
does not affect published versions. Bump it occasionally so local builds stay
recognizable, but it requires no synchronization with releases.

## One-time setup

The workflow needs a NuGet API key with push access to the `DurableStack.*`
package IDs, stored as a GitHub Actions secret:

1. Create an API key at nuget.org (scope: *Push new packages and package
   versions*, glob `DurableStack.*`).
2. In the GitHub repository: **Settings → Secrets and variables → Actions →
   New repository secret**, name `NUGET_API_KEY`.

## Fixing a bad release

Published NuGet versions are immutable — they can be unlisted but never
replaced. To fix a bad release, tag and publish a new patch version. Re-running
a release workflow is safe: `--skip-duplicate` makes already-published versions
a no-op.

## Upgrade and rollback expectations

- DurableStack migrations are versioned (see the
  `durable_stack_schema_migrations` table), applied exactly once under a
  provider-native migration lock, and safe under concurrent worker startup.
- Upgrades are forward-safe from prior releases on the same provider.
- Rollback is operational, not schema-down migration: restore the database
  from a backup/snapshot taken before upgrade.
- For production, run migrations in deployment steps before rolling app
  instances.

## Notes

- The `ci` workflow builds and tests every push and pull request but never
  publishes; only `release` (tag push) publishes.
- Keep docs and examples aligned with any API or behavior changes included in
  the release.
