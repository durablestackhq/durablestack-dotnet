using System;

namespace DurableStack.Core;

public sealed class JobContext
{
    public Guid RunId { get; init; }

    public string JobName { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public DateTimeOffset ScheduledForUtc { get; init; }

    public IServiceProvider Services { get; init; } = default!;
}
