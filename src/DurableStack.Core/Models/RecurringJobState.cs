using System;

namespace DurableStack.Core.Models;

public sealed class RecurringJobState
{
    public required string JobName { get; init; }

    public required string CronExpression { get; init; }

    public required string TimeZone { get; init; }

    public DateTimeOffset NextRunAtUtc { get; set; }
}
