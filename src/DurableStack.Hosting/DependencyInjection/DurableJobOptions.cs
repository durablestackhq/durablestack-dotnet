using System;
using DurableStack.Core.Models;

namespace DurableStack.Hosting.DependencyInjection;

/// <summary>
/// Fluent per-job settings applied when registering a job with the
/// <c>AddDurableJob</c> overloads that take a configuration callback.
/// </summary>
public sealed class DurableJobOptions
{
    /// <summary>
    /// Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.
    /// </summary>
    public int MaxAttempts { get; private set; } = 3;

    /// <summary>
    /// Cron expression that makes the job recurring, or <see langword="null"/> for on-demand jobs.
    /// Set via <see cref="RunOnCron"/>.
    /// </summary>
    public string? CronExpression { get; private set; }

    /// <summary>
    /// IANA time zone identifier in which <see cref="CronExpression"/> is evaluated. Defaults to "UTC".
    /// </summary>
    public string TimeZone { get; private set; } = "UTC";

    /// <summary>
    /// Whether a new recurring occurrence may be enqueued while a previous run of the same job is still active.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowConcurrentRuns { get; private set; }

    /// <summary>
    /// How the delay between retry attempts is calculated, or <see langword="null"/> to use the worker-level default.
    /// </summary>
    public RetryBehavior? RetryBehavior { get; private set; }

    /// <summary>
    /// Initial delay in seconds before the first retry, or <see langword="null"/> to use the worker-level default.
    /// </summary>
    public int? RetryInitialDelaySeconds { get; private set; }

    /// <summary>
    /// Sets the maximum number of execution attempts for the job.
    /// </summary>
    /// <param name="maxAttempts">Attempt limit; must be greater than zero.</param>
    /// <returns>The same options instance for chaining.</returns>
    public DurableJobOptions WithMaxAttempts(int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than zero.");
        }

        MaxAttempts = maxAttempts;
        return this;
    }

    /// <summary>
    /// Makes the job recurring on the given cron schedule.
    /// </summary>
    /// <param name="cronExpression">Cron expression that determines when new runs are materialized.</param>
    /// <param name="timeZone">IANA time zone identifier in which the cron expression is evaluated. Defaults to "UTC".</param>
    /// <returns>The same options instance for chaining.</returns>
    public DurableJobOptions RunOnCron(string cronExpression, string timeZone = "UTC")
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException("Cron expression is required.", nameof(cronExpression));
        }

        if (string.IsNullOrWhiteSpace(timeZone))
        {
            throw new ArgumentException("Time zone is required.", nameof(timeZone));
        }

        CronExpression = cronExpression;
        TimeZone = timeZone;
        return this;
    }

    /// <summary>
    /// Controls whether recurring occurrences may overlap an active run of the same job.
    /// </summary>
    /// <param name="allowConcurrentRuns"><see langword="true"/> to allow overlapping runs; <see langword="false"/> to skip occurrences while a run is active.</param>
    /// <returns>The same options instance for chaining.</returns>
    public DurableJobOptions WithAllowConcurrentRuns(bool allowConcurrentRuns = true)
    {
        AllowConcurrentRuns = allowConcurrentRuns;
        return this;
    }

    /// <summary>
    /// Sets how the delay between retry attempts is calculated for this job.
    /// </summary>
    /// <param name="retryBehavior">Fixed delay or exponential backoff.</param>
    /// <returns>The same options instance for chaining.</returns>
    public DurableJobOptions WithRetryBehavior(RetryBehavior retryBehavior)
    {
        RetryBehavior = retryBehavior;
        return this;
    }

    /// <summary>
    /// Sets the initial delay before the first retry for this job.
    /// </summary>
    /// <param name="retryInitialDelaySeconds">Delay in seconds; must be greater than zero.</param>
    /// <returns>The same options instance for chaining.</returns>
    public DurableJobOptions WithRetryInitialDelaySeconds(int retryInitialDelaySeconds)
    {
        if (retryInitialDelaySeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInitialDelaySeconds), "Retry initial delay must be greater than zero.");
        }

        RetryInitialDelaySeconds = retryInitialDelaySeconds;
        return this;
    }
}
