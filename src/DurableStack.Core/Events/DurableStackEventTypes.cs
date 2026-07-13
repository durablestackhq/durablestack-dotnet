namespace DurableStack.Core.Events;

/// <summary>
/// Well-known values for <see cref="DurableStackEvent.EventType"/> and the current event
/// schema version.
/// </summary>
public static class DurableStackEventTypes
{
    /// <summary>Schema version stamped onto every emitted <see cref="DurableStackEvent"/>.</summary>
    public const int CurrentVersion = 2;

    /// <summary>A worker claimed a due run from the store under a lease.</summary>
    public const string JobClaimed = "job_claimed";
    /// <summary>Execution of a claimed run began on the worker.</summary>
    public const string JobStarted = "job_started";
    /// <summary>A run completed successfully and its outcome was recorded.</summary>
    public const string JobSucceeded = "job_succeeded";
    /// <summary>A run threw an exception and the failure was recorded.</summary>
    public const string JobFailed = "job_failed";
    /// <summary>A failed run was rescheduled for another attempt.</summary>
    public const string JobRetried = "job_retried";
    /// <summary>A retry attempt was scheduled; the event carries the retry timestamp.</summary>
    public const string RetryScheduled = "retry_scheduled";
    /// <summary>A periodic liveness signal from a worker.</summary>
    public const string WorkerHeartbeat = "worker_heartbeat";
}
