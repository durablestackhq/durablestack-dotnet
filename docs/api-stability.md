# API Stability (1.0 Freeze)

This document defines the DurableStack .NET API surface considered stable for `1.x`.

## Stability policy

- Public APIs listed here are stable for `1.x`.
- Breaking changes to these contracts require a major version bump.
- Additive changes (new methods/options/events) are allowed in minor versions.

## Core contracts

### `IDurableStackClient`

- `Task<Guid> EnqueueAsync<TJob>(object? payload = null, CancellationToken cancellationToken = default)`
- `Task<Guid> ScheduleAsync<TJob>(object? payload, DateTimeOffset runAtUtc, CancellationToken cancellationToken = default)`

### `IDurableScheduleAdminService`

- `Task<IReadOnlyList<RecurringJobState>> ListScheduledJobsAsync(bool includeDisabled = true, CancellationToken cancellationToken = default)`
- `Task<bool> SetScheduledJobEnabledAsync(string jobName, bool enabled, CancellationToken cancellationToken = default)`
- `Task<bool> UpdateScheduledJobCronAsync(string jobName, string cronExpression, string timeZone = "UTC", CancellationToken cancellationToken = default)`
- `Task<Guid?> RunScheduledJobNowAsync(string jobName, CancellationToken cancellationToken = default)`

### `IDurableJobRunQueryService`

- `Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take = 100, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(string status, int take = 100, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(string jobName, int take = 100, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take = 100, CancellationToken cancellationToken = default)`

## Stable options

`DurableStackOptions`:

- `StorageProvider`
- `WorkerName`
- `ConnectionStringName`
- `DatabaseTablePrefix`
- `PollInterval` / `PollIntervalSeconds`
- `PollJitterEnabled`
- `PollJitterRatio`
- `BatchSize`
- `LeaseDuration` / `LeaseDurationSeconds`
- `RetryDelay`
- `JobActivation`
- `Recurring`
- `Retention`
- `Eventing`
- `JobRegistration`

`DurableStackEventingOptions`:

- `TenantId`, `ClientSecret`, `Environment`, `ServiceName`
- `IngestionApiBaseUrl`, `IngestionPath`
- `IngestionMaxBatchSize`, `IngestionMaxRequestBodyBytes`, `IngestionMaxRetryAttempts`
- `IngestionFlushInterval` / `IngestionFlushIntervalSeconds`
- `IncludeErrorDetail`, `MaxErrorDetailLength`

## HTTP examples contract

Example API behavior considered stable:

- enqueue endpoints return `202` with `{ runId }`
- run-now schedule endpoint returns `202` with `{ runId }` when queued
- `GET /runs/{id}` supports run status polling by run id

## Notes

- Internal types and private implementation details are not covered by this stability guarantee.
- Database schema may evolve with additive migrations; destructive migration changes require a documented major-version plan.

For release procedure and validation steps, see `docs/releasing.md`.
