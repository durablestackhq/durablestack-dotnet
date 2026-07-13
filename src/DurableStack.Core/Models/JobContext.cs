using System;

namespace DurableStack.Core;

/// <summary>
/// Per-execution information handed to a job's <c>ExecuteAsync</c>: which run and attempt
/// is executing, when it was due, and a service provider for resolving dependencies.
/// </summary>
public sealed class JobContext
{
    /// <summary>
    /// Id of the persisted run being executed. Stable across retries, which makes it a
    /// useful idempotency key for at-least-once side effects.
    /// </summary>
    public Guid RunId { get; init; }

    /// <summary>
    /// The registered name of the job being executed.
    /// </summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// The 1-based attempt number of this execution; values above 1 indicate a retry after
    /// an earlier failure or an expired lease.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// The UTC time the run was due to execute. Actual start time can be later, depending on
    /// worker polling and load.
    /// </summary>
    public DateTimeOffset ScheduledForUtc { get; init; }

    /// <summary>
    /// Service provider for resolving dependencies during execution. With the default
    /// scoped-per-execution activation mode this is a dedicated scope disposed when the
    /// attempt finishes; in root-provider mode it is the application's root provider.
    /// </summary>
    public IServiceProvider Services { get; init; } = default!;
}
