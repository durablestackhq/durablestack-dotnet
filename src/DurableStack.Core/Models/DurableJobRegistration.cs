using System;

namespace DurableStack.Core.Models;

public sealed class DurableJobRegistration
{
    public required string JobName { get; init; }

    public required Type JobType { get; init; }

    public Type? PayloadType { get; init; }

    public int MaxAttempts { get; init; } = 3;

    public string? CronExpression { get; init; }

    public string TimeZone { get; init; } = "UTC";

    public bool IsRecurring => !string.IsNullOrWhiteSpace(CronExpression);
}
