namespace DurableStack.Core.Options;

/// <summary>
/// Controls the startup reconciliation between recurring jobs declared in code and the
/// schedule rows already persisted in the store.
/// </summary>
public sealed class DurableStackRecurringRegistrationSyncOptions
{
    /// <summary>
    /// What happens when a code registration matches a schedule row that already exists.
    /// Defaults to <see cref="ExistingRecurringJobBehavior.KeepDatabase"/>, which preserves
    /// runtime edits made through the admin service.
    /// </summary>
    public ExistingRecurringJobBehavior ExistingJobBehavior { get; set; } = ExistingRecurringJobBehavior.KeepDatabase;

    /// <summary>
    /// What happens to stored schedule rows whose job name is no longer registered in code.
    /// Defaults to <see cref="OrphanedRecurringJobBehavior.Disable"/>, which stops them from
    /// materializing runs that no job could execute.
    /// </summary>
    public OrphanedRecurringJobBehavior OrphanedJobBehavior { get; set; } = OrphanedRecurringJobBehavior.Disable;
}
