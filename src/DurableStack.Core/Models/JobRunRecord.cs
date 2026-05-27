using System;

namespace DurableStack.Core.Models;

public sealed class JobRunRecord
{
    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public string JobType { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public DateTimeOffset ScheduledForUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public string? PayloadJson { get; set; }

    public string? ErrorMessage { get; set; }
}
