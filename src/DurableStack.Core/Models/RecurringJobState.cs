using System;

namespace DurableStack.Core.Models;

/// <summary>
/// The persisted schedule row for one recurring job: its cron settings plus the next due
/// time that drives materialization. Rows are created from code registrations at startup
/// and can afterwards be paused, resumed, or rescheduled at run time via the admin service.
/// </summary>
public sealed class RecurringJobState
{
    /// <summary>
    /// Stable name identifying both the schedule row and the registered job it triggers.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Assembly-qualified name of the job's CLR type at registration time, stored for
    /// diagnostics.
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// Five-field cron expression that determines when new runs are due.
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// IANA time zone id the cron expression is evaluated in.
    /// </summary>
    public required string TimeZone { get; init; }

    /// <summary>
    /// Total attempts (initial execution plus retries) each materialized run is allowed
    /// before it is terminally failed.
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// Whether the schedule is active. Defaults to true; disabled schedules are kept in the
    /// store but never materialized into runs.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When false, a new occurrence is skipped while a previous run of this job is still
    /// pending or executing.
    /// </summary>
    public bool AllowConcurrentRuns { get; init; }

    /// <summary>
    /// Per-job retry strategy (fixed delay or exponential backoff) applied to materialized
    /// runs; null falls back to fixed-delay using the global
    /// <c>DurableStackOptions.RetryDelay</c>.
    /// </summary>
    public RetryBehavior? RetryBehavior { get; init; }

    /// <summary>
    /// Base delay in seconds before the first retry of a failed run; null falls back to the
    /// global <c>DurableStackOptions.RetryDelay</c>.
    /// </summary>
    public int? RetryInitialDelaySeconds { get; init; }

    /// <summary>
    /// UTC time of the next cron slot to materialize. Also serves as the optimistic
    /// concurrency token that guarantees each slot produces at most one run across
    /// competing workers.
    /// </summary>
    public DateTimeOffset NextRunAtUtc { get; set; }
}
