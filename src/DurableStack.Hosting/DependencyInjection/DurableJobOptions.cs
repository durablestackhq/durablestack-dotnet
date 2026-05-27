using System;

namespace DurableStack.Hosting.DependencyInjection;

public sealed class DurableJobOptions
{
    public int MaxAttempts { get; private set; } = 3;

    public string? CronExpression { get; private set; }

    public string TimeZone { get; private set; } = "UTC";

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
}
