namespace DurableStack.Core.Options;

public sealed class DurableStackRecurringRegistrationSyncOptions
{
    public ExistingRecurringJobBehavior ExistingJobBehavior { get; set; } = ExistingRecurringJobBehavior.KeepDatabase;

    public OrphanedRecurringJobBehavior OrphanedJobBehavior { get; set; } = OrphanedRecurringJobBehavior.Disable;
}
