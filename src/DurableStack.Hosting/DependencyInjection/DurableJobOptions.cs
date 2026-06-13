using System;
using DurableStack.Core.Models;

namespace DurableStack.Hosting.DependencyInjection;

public sealed class DurableJobOptions
{
    public int MaxAttempts { get; private set; } = 3;

    public string? CronExpression { get; private set; }

    public string TimeZone { get; private set; } = "UTC";

    public bool AllowConcurrentRuns { get; private set; }

    public RetryBehavior? RetryBehavior { get; private set; }

    public int? RetryInitialDelaySeconds { get; private set; }

    public DurableJobOptions WithMaxAttempts(int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than zero.");
        }

        MaxAttempts = maxAttempts;
        return this;
    }

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

    public DurableJobOptions WithAllowConcurrentRuns(bool allowConcurrentRuns = true)
    {
        AllowConcurrentRuns = allowConcurrentRuns;
        return this;
    }

    public DurableJobOptions WithRetryBehavior(RetryBehavior retryBehavior)
    {
        RetryBehavior = retryBehavior;
        return this;
    }

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
