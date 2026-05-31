# Data Retention

DurableStack can automatically remove historical terminal runs so memory and database tables do not grow without bound.

## What gets pruned

Only terminal runs are pruned:

- `succeeded`
- `failed`

Pending or leased runs are never removed by retention cleanup.

Recurring schedule definitions are also never removed by retention cleanup.

## Defaults

- In-memory provider retention: `1 hour`
- Database provider retention: `24 hours`
- Sweep interval: `5 minutes`
- Delete batch size: `1000`

## Configuration

```json
{
  "DurableStack": {
    "Retention": {
      "Enabled": true,
      "RunRetentionSeconds": 86400,
      "SweepIntervalSeconds": 300,
      "DeleteBatchSize": 1000
    }
  }
}
```

Notes:

- `RunRetentionSeconds` is provider-aware when omitted (1h for in-memory, 24h for DB providers).
- Invalid values are normalized to defaults.
- Cleanup runs in bounded batches using `DeleteBatchSize`.

For runtime schedule operations (disable/enable/run-now/cron updates), see `docs/scheduled-job-management.md`.
