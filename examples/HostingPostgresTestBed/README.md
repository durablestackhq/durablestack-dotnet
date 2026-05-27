# HostingPostgresTestBed

`HostingPostgresTestBed` is a local sandbox app for exercising DurableStack behavior against PostgreSQL.

It is intended for API/App integration testing where you want predictable job outcomes (success, retry, terminal failure), recurring runs, and long-running lease scenarios.

## What this project is for

- Validate DurableStack storage, worker leasing, retries, and run status transitions.
- Generate controlled telemetry patterns for downstream dashboards and API integration tests.
- Provide a safe place to test job payloads and retry behavior without modifying production apps.

## Job registrations

- `send-welcome-email` (`SendWelcomeEmailJob`, args: `SendWelcomeEmailArgs`)
  - Max attempts: `3`
- `heartbeat-every-minute` (`HeartbeatJob`, no args)
  - Recurring cron: `* * * * *` (UTC)
  - Max attempts: `3`
- `long-running-lease-demo` (`LongRunningLeaseDemoJob`, no args)
  - Max attempts: `3`
  - Simulates long execution (`~20s`) to exercise lease extension behavior.
- `flaky-failure-demo` (`FlakyFailureDemoJob`, args: `FlakyFailureDemoArgs`)
  - Default registration max attempts: `5`
  - Fails while `Attempt <= FailUntilAttempt`, then succeeds.

## HTTP endpoints

- `GET /`
  - Health/info text for the test bed.

- `POST /migrate`
  - Applies DurableStack store migrations.

- `POST /enqueue?email=<address>`
  - Enqueues `send-welcome-email`.

- `POST /enqueue-long-running`
  - Enqueues `long-running-lease-demo`.

- `POST /enqueue-fail-always`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=10` (expected terminal failure with current retry policy).

- `POST /enqueue-fail-once`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=1` (fails first attempt, then succeeds).

- `POST /enqueue-fail-twice`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=2` (fails twice, then succeeds).

- `POST /enqueue-fail-custom?failUntilAttempt=<n>&maxAttempts=<n>&scenarioName=<name>`
  - Enqueues `flaky-failure-demo` with custom failure pattern.
  - `failUntilAttempt` default: `1`, allowed range: `0..100`.
  - `maxAttempts` default: registration default (`5`), allowed range: `1..25`.
  - `scenarioName` optional; if omitted, a descriptive name is generated.
  - This endpoint overrides `maxAttempts` per run by enqueueing through `IDurableJobStore`.

- `GET /runs`
  - Returns up to 50 recent runs.

- `GET /runs/{id}`
  - Returns details for a specific run id.

- `GET /runs/status/{status}?take=<n>`
  - Returns runs by status (`pending`, `leased`, `succeeded`, `failed`).
  - `take` default: `50`, range: `1..500`.

## Notes

- Configuration placeholders for integration testing are in `appsettings.json` and `appsettings.Development.json` (`TenantId`, `ClientSecret`).
- API ingestion is automatically enabled when `DurableStack:Eventing:TenantId` and `DurableStack:Eventing:ClientSecret` are configured.
- `UseDurableStackLoggingEventSink()` is optional and additive for local debugging; it does not replace API ingestion.
- To target non-default API endpoints, set `DurableStack:Eventing:IngestionApiBaseUrl` and optionally `DurableStack:Eventing:IngestionPath`.
