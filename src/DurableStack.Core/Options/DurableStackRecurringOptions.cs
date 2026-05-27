namespace DurableStack.Core.Options;

public sealed class DurableStackRecurringOptions
{
    public RecurringCatchUpPolicy CatchUpPolicy { get; set; } = RecurringCatchUpPolicy.SkipMissed;
}
