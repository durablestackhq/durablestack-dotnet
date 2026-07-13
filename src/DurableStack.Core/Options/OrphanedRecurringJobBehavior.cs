namespace DurableStack.Core.Options;

/// <summary>
/// What startup registration sync does with schedule rows in the store whose job name is no
/// longer registered in code (for example after a job was deleted or renamed).
/// </summary>
public enum OrphanedRecurringJobBehavior
{
    /// <summary>
    /// Disables the orphaned schedule so it stops materializing runs that no registered job
    /// could execute. The row is kept and can be re-enabled if the registration returns.
    /// This is the default.
    /// </summary>
    Disable = 0,

    /// <summary>
    /// Leaves orphaned schedules untouched; they keep materializing runs, which then fail at
    /// execution time because no registration resolves the job type. Useful in mixed
    /// deployments where another process still registers the job.
    /// </summary>
    Ignore = 1,
}
