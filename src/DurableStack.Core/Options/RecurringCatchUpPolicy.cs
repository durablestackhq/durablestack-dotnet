namespace DurableStack.Core.Options;

/// <summary>
/// What happens to cron occurrences of a recurring job that came due while no worker was
/// running (deployment gaps, outages).
/// </summary>
public enum RecurringCatchUpPolicy
{
    /// <summary>
    /// Fast-forwards the schedule past every missed occurrence and resumes at the next
    /// future one, so an outage produces no burst of stale runs. This is the default.
    /// </summary>
    SkipMissed = 0,

    /// <summary>
    /// Executes missed occurrences: each poll materializes one overdue slot at a time until
    /// the schedule has worked through the backlog. Use when every slot must run, such as
    /// per-period billing or report generation.
    /// </summary>
    CatchUp = 1,
}
