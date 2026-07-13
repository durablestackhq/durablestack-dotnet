using System;

namespace DurableStack.Core.Events;

/// <summary>
/// An immutable record of something that happened in the job pipeline (claimed, started,
/// succeeded, failed, retried, ...). Instances are produced by
/// <see cref="DurableStackEventFactory"/> and delivered to every registered
/// <c>IDurableStackEventSink</c>.
/// </summary>
public sealed class DurableStackEvent
{
    /// <summary>Unique identifier of this event instance; useful for deduplication in downstream consumers.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>The kind of occurrence, as one of the <see cref="DurableStackEventTypes"/> constants.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Schema version of the event payload, allowing consumers to handle format changes.</summary>
    public int EventVersion { get; init; } = DurableStackEventTypes.CurrentVersion;

    /// <summary>When the event was created, in UTC.</summary>
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Identifier of the job run the event relates to, when the event concerns a specific run.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Registered name of the job the event relates to, when applicable.</summary>
    public string? JobName { get; init; }

    /// <summary>The run's attempt number (1-based) at the time the event was emitted.</summary>
    public int? Attempt { get; init; }

    /// <summary>Maximum number of attempts the run is allowed before it fails terminally.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>Name of the worker that emitted the event.</summary>
    public string? WorkerName { get; init; }

    /// <summary>Tenant identifier from the eventing configuration, used to attribute events in multi-tenant ingestion.</summary>
    public string? TenantId { get; init; }

    /// <summary>Logical service name from the eventing configuration, identifying the emitting application.</summary>
    public string? ServiceName { get; init; }

    /// <summary>W3C trace id of the ambient <c>Activity</c> at emission time, correlating the event with distributed traces.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C span id of the ambient <c>Activity</c> at emission time.</summary>
    public string? SpanId { get; init; }

    /// <summary>Elapsed execution time in milliseconds, populated on completion events.</summary>
    public double? DurationMs { get; init; }

    /// <summary>When the next retry attempt is scheduled to run, in UTC, for retry-related events.</summary>
    public DateTimeOffset? RetryAtUtc { get; init; }

    /// <summary>Full type name of the exception that caused a failure. Always safe to emit; carries no message content.</summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// The exception message. Only populated when
    /// <c>Eventing.IncludeErrorDetail</c> is enabled — exception messages routinely
    /// contain sensitive values (connection details, file paths, business data), so
    /// by default only <see cref="ErrorType"/> leaves the process.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The full exception detail (including stack trace). Like <see cref="ErrorMessage"/>,
    /// this is only populated when <c>Eventing.IncludeErrorDetail</c> is enabled, and is
    /// truncated to the configured maximum length.
    /// </summary>
    public string? ErrorDetail { get; init; }

    /// <summary>Free-form informational text attached to the event, such as the scheduled retry timestamp.</summary>
    public string? Message { get; init; }
}
