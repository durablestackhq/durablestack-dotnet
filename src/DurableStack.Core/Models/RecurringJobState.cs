using System;

namespace DurableStack.Core.Models;

public sealed class RecurringJobState
{
    public required string JobName { get; init; }

    public required string JobType { get; init; }

    public required string CronExpression { get; init; }

    public required string TimeZone { get; init; }

    public int MaxAttempts { get; init; }

    public bool Enabled { get; init; } = true;

    public DateTimeOffset NextRunAtUtc { get; set; }
}
