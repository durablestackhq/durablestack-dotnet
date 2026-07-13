namespace DurableStack.Core.Options;

/// <summary>
/// Settings for recurring (cron) jobs: how occurrences missed during downtime are treated
/// and how code registrations reconcile with schedule rows already in the store.
/// </summary>
public sealed class DurableStackRecurringOptions
{
    /// <summary>
    /// What happens to cron occurrences that came due while no worker was running. Defaults
    /// to <see cref="RecurringCatchUpPolicy.SkipMissed"/>, which fast-forwards past them.
    /// </summary>
    public RecurringCatchUpPolicy CatchUpPolicy { get; set; } = RecurringCatchUpPolicy.SkipMissed;

    /// <summary>
    /// How code-declared recurring registrations are reconciled at startup with schedule
    /// rows already stored in the database.
    /// </summary>
    public DurableStackRecurringRegistrationSyncOptions RegistrationSync { get; } = new();
}
