namespace DurableStack.Core.Options;

/// <summary>
/// What startup registration sync does when a recurring job declared in code already has a
/// schedule row in the store.
/// </summary>
public enum ExistingRecurringJobBehavior
{
    /// <summary>
    /// Leaves the stored row untouched, preserving cron, time zone, and enabled/disabled
    /// changes made at run time through the admin service. Code changes to the schedule are
    /// ignored for existing rows. This is the default.
    /// </summary>
    KeepDatabase = 0,

    /// <summary>
    /// Overwrites the stored row with the code registration's settings on every startup,
    /// discarding runtime edits. Use when code is the single source of truth for schedules.
    /// </summary>
    UpdateFromCode = 1,
}
