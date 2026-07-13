using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Syncs code-declared recurring job registrations to schedule rows in the store at startup.
/// How conflicts with existing rows and schedules no longer present in code are handled is
/// controlled by <c>DurableStackRecurringOptions.RegistrationSync</c>.
/// </summary>
public interface IRecurringJobInitializer
{
    /// <summary>
    /// Upserts schedule rows for the registered recurring jobs (computing each schedule's
    /// first due time from its cron expression) and, depending on configuration, disables
    /// schedule rows whose job is no longer registered in code.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);
}
