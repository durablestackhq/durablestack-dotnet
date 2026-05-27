# Reliability Model

DurableStack uses lease-based claiming and explicit run state transitions to provide distributed-safe execution.

## Run execution model

- workers claim only `pending` runs that are due
- a claim increments attempt and sets lease ownership
- while a run is executing, the owning worker periodically extends lease expiry
- successful completion marks `succeeded` and clears lease
- failure marks `pending` with retry schedule, or `failed` when attempts are exhausted

If a worker crashes or stops extending lease before completion, another worker can reclaim the run after lease expiry.

## LeaseDuration guidance

`DurableStackOptions.LeaseDuration` should be longer than expected transient pauses (GC, brief CPU contention, short network hiccups).

- set lease duration comfortably above normal runtime jitter
- lease heartbeat extends active leases roughly every half lease duration
- for long-running jobs, use a lease duration that avoids frequent near-expiry windows

Practical starting point:

- start with `30s` for general workloads
- lower values (for example `5s`) are useful in demos to observe lease extension behavior

## Recurring run idempotency

Recurring schedules are materialized as concrete job runs with a slot identity.

- recurring runs set `schedule_slot_utc` to the exact slot timestamp
- one-off runs leave `schedule_slot_utc` null
- PostgreSQL enforces uniqueness on `(job_name, schedule_slot_utc)` when `schedule_slot_utc is not null`

This prevents duplicate recurring run materialization for the same job and slot under multi-worker concurrency.

## Recurring missed-slot behavior

`DurableStackOptions.Recurring.CatchUpPolicy` controls how missed recurring slots are handled.

- `SkipMissed` (default): schedule continues from the next future slot
- `CatchUp`: replay missed slots one per processing loop until caught up
