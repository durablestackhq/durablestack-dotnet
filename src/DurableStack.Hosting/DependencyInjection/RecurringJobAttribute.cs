using System;

namespace DurableStack.Hosting.DependencyInjection;

/// <summary>
/// Marks an auto-discovered job class as recurring, scheduling it on a cron expression.
/// The cron expression and time zone are validated when the assembly is scanned at registration time.
/// Combine with <see cref="DurableJobAttribute"/> to customize the job name and retry settings.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RecurringJobAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute with the cron expression that drives the recurring schedule.
    /// </summary>
    /// <param name="cron">Cron expression evaluated in <see cref="TimeZone"/> to compute run occurrences.</param>
    public RecurringJobAttribute(string cron)
    {
        Cron = cron;
    }

    /// <summary>
    /// Cron expression that determines when new runs are materialized.
    /// </summary>
    public string Cron { get; }

    /// <summary>
    /// IANA time zone identifier in which the cron expression is evaluated. Defaults to "UTC".
    /// Invalid identifiers cause registration to fail.
    /// </summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>
    /// Whether the recurring schedule starts enabled. Defaults to <see langword="true"/>;
    /// disabled schedules can later be enabled through the schedule admin service.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether a new occurrence may be enqueued while a previous run of the same job is still active.
    /// Defaults to <see langword="false"/>, which skips occurrences that would overlap.
    /// </summary>
    public bool AllowConcurrentRuns { get; init; }
}
