using System;

namespace DurableStack.Core.Events;

public sealed class DurableStackEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType { get; init; } = string.Empty;

    public int EventVersion { get; init; } = DurableStackEventTypes.CurrentVersion;

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public Guid? RunId { get; init; }

    public string? JobName { get; init; }

    public int? Attempt { get; init; }

    public string? WorkerName { get; init; }

    public string? TenantId { get; init; }

    public string? Environment { get; init; }

    public string? ServiceName { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public double? DurationMs { get; init; }

    public DateTimeOffset? RetryAtUtc { get; init; }

    public string? ErrorType { get; init; }

    public string? ErrorDetail { get; init; }

    public string? Message { get; init; }
}
