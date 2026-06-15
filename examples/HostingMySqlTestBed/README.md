# HostingMySqlTestBed

`HostingMySqlTestBed` is a local sandbox app for exercising DurableStack behavior against MySQL.

It is intended for API/App integration testing where you want predictable job outcomes (success, retry, terminal failure), recurring runs, and long-running lease scenarios.

## HTTP endpoints

- `GET /`
  - Health/info text for the test bed.

- `POST /migrate`
  - Applies DurableStack store migrations.

- `POST /enqueue?email=<address>`
  - Enqueues `send-welcome-email`.
  - Returns `202` with `{ runId }`.

- `POST /enqueue-long-running`
  - Enqueues `long-running-lease-demo`.
  - Returns `202` with `{ runId }`.

- `POST /enqueue-fail-always`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=10`.
  - Returns `202` with `{ runId }`.

- `POST /enqueue-fail-once`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=1`.
  - Returns `202` with `{ runId }`.

- `POST /enqueue-fail-twice`
  - Enqueues `flaky-failure-demo` with `FailUntilAttempt=2`.
  - Returns `202` with `{ runId }`.

- `POST /enqueue-fail-custom?failUntilAttempt=<n>&maxAttempts=<n>&scenarioName=<name>`
  - Enqueues `flaky-failure-demo` with custom failure pattern and per-run max attempts override.
  - Returns `202` with `{ runId }`.

- `GET /runs`
  - Returns up to 50 recent runs.

- `GET /runs/{id}`
  - Returns details for a specific run id.

- `GET /runs/status/{status}?take=<n>`
  - Returns runs by status (`pending`, `leased`, `succeeded`, `failed`).

## Notes

- This test bed focuses on enqueue/retry/failure flow and run-state observability.
- For schedule management endpoint examples, see `examples/HostingPostgresTestBed/README.md`.
