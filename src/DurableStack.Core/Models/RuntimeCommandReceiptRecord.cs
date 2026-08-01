using System;

namespace DurableStack.Core.Models;

/// <summary>
/// Local runtime receipt for a control command delivered by the platform.
/// One row per command id, used for dedupe and reporting command outcomes.
/// </summary>
public sealed class RuntimeCommandReceiptRecord
{
    /// <summary>
    /// Platform-generated unique id for the runtime command.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Runtime processing status for this command receipt.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Optional machine-readable error code when command processing fails.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Optional human-readable error message when command processing fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Optional run id created by an execute-now command.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// Timestamp when the runtime last recorded this receipt state.
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <summary>
    /// Timestamp when command processing completed, if completed.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
}
