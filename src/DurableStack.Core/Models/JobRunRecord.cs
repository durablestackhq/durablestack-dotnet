using System;

namespace DurableStack.Core.Models;

/// <summary>
/// The persisted state of one job run — a single enqueued unit of work and its lifecycle
/// from pending through leased execution to a terminal succeeded or failed state. Instances
/// returned from queries are snapshots; the stored row may change after they are read.
/// </summary>
public sealed class JobRunRecord
{
    /// <summary>
    /// Unique id of the run, assigned when it is enqueued and stable across retries.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The registered job name used to resolve the executing type when the run is claimed.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly-qualified name of the job's CLR type at enqueue time, stored for diagnostics.
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle state: "pending" (waiting or awaiting retry), "leased" (claimed by a
    /// worker), "succeeded", or "failed" (terminal, including cancellations and poison
    /// quarantine).
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Earliest UTC time the run may execute. Moved forward to the retry time when a failed
    /// attempt is rescheduled.
    /// </summary>
    public DateTimeOffset ScheduledForUtc { get; set; }

    /// <summary>
    /// For runs materialized from a recurring schedule, the cron slot that produced them;
    /// null for runs enqueued directly.
    /// </summary>
    public DateTimeOffset? ScheduleSlotUtc { get; set; }

    /// <summary>
    /// UTC time the run was first claimed by a worker; null while it is still waiting.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>
    /// UTC time the run reached a terminal state (succeeded or failed); null while it is
    /// pending, retrying, or executing.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Number of times the run has been claimed for execution; 0 before the first claim.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// Total attempts allowed before the run is terminally failed.
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// Name of the worker currently holding the run's lease; null when the run is not
    /// leased. Completion writes from any other worker are rejected.
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// UTC time the current lease expires. Heartbeats push it forward; once it passes, other
    /// workers may reclaim the run (or quarantine it when no attempts remain).
    /// </summary>
    public DateTimeOffset? LeaseUntilUtc { get; set; }

    /// <summary>
    /// JSON-serialized job arguments captured at enqueue time; null for jobs without a
    /// payload.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Message from the most recent failure (or cancellation); null after a success and
    /// before any failure.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
