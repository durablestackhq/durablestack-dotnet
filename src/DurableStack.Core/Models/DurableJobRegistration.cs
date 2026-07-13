using System;

namespace DurableStack.Core.Models;

/// <summary>
/// An immutable entry in the in-process job catalog: binds a stable job name to the CLR
/// type that executes it, along with retry, payload, and (for recurring jobs) cron
/// settings. Recurring registrations are synced to schedule rows in the store at startup.
/// </summary>
public sealed class DurableJobRegistration
{
    /// <summary>
    /// Stable, unique name stored with every run of this job; renaming it orphans runs
    /// already persisted under the old name.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// The CLR type resolved from dependency injection to execute runs of this job. Must
    /// implement <c>IDurableJob</c> or <c>IDurableJob&lt;TArgs&gt;</c>.
    /// </summary>
    public required Type JobType { get; init; }

    /// <summary>
    /// The argument type deserialized from a run's stored JSON before execution, or null
    /// when the job takes no typed payload.
    /// </summary>
    public Type? PayloadType { get; init; }

    /// <summary>
    /// Total attempts (initial execution plus retries) before a run of this job is
    /// terminally failed. Defaults to 3.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Five-field cron expression that drives run creation for recurring jobs; null for
    /// fire-and-forget jobs.
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// IANA time zone id the cron expression is evaluated in. Defaults to "UTC".
    /// </summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>
    /// When false (the default), a new recurring occurrence is skipped while a previous run
    /// of this job is still pending or executing.
    /// </summary>
    public bool AllowConcurrentRuns { get; init; }

    /// <summary>
    /// Whether a recurring schedule starts out active. Defaults to true; disabled schedules
    /// are stored but never materialized into runs.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Per-job retry strategy (fixed delay or exponential backoff); null falls back to
    /// fixed-delay using the global <c>DurableStackOptions.RetryDelay</c>.
    /// </summary>
    public RetryBehavior? RetryBehavior { get; init; }

    /// <summary>
    /// Base delay in seconds before the first retry of a failed run; null (or a non-positive
    /// value) falls back to the global <c>DurableStackOptions.RetryDelay</c>. Under backoff
    /// this base doubles on each subsequent attempt.
    /// </summary>
    public int? RetryInitialDelaySeconds { get; init; }

    /// <summary>
    /// True when the registration carries a cron expression and therefore represents a
    /// recurring schedule rather than a fire-and-forget job.
    /// </summary>
    public bool IsRecurring => !string.IsNullOrWhiteSpace(CronExpression);
}
