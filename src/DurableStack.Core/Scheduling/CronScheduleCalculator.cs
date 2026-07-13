using System;
using Cronos;

namespace DurableStack.Core.Scheduling;

/// <summary>
/// Computes cron occurrences using Cronos. Accepts standard 5-field expressions and 6-field
/// expressions with a leading seconds field; the field count selects the format
/// automatically.
/// </summary>
public static class CronScheduleCalculator
{
    /// <summary>
    /// Returns the next occurrence strictly after <paramref name="fromUtc"/>, evaluated in
    /// the given time zone and returned as UTC.
    /// </summary>
    /// <param name="cronExpression">A 5-field or 6-field (with seconds) cron expression.</param>
    /// <param name="timeZone">Time zone identifier the schedule is evaluated in, resolved by <see cref="TimeZoneResolver"/>.</param>
    /// <param name="fromUtc">Exclusive UTC starting point of the search.</param>
    /// <returns>The next occurrence in UTC.</returns>
    /// <exception cref="InvalidOperationException">
    /// The expression does not have 5 or 6 fields, or it yields no future occurrence.
    /// </exception>
    public static DateTimeOffset GetNextOccurrenceUtc(string cronExpression, string timeZone, DateTimeOffset fromUtc)
    {
        var expression = CronExpression.Parse(cronExpression, ResolveCronFormat(cronExpression));
        var zone = TimeZoneResolver.ResolveFromIana(timeZone);

        var next = expression.GetNextOccurrence(fromUtc.UtcDateTime, zone, inclusive: false);
        if (next is null)
        {
            throw new InvalidOperationException($"No next cron occurrence was found for expression '{cronExpression}'.");
        }

        return new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc));
    }

    private static CronFormat ResolveCronFormat(string cronExpression)
    {
        var fields = cronExpression.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return fields.Length switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => throw new InvalidOperationException(
                $"Cron expression '{cronExpression}' has {fields.Length} fields. DurableStack supports 5-field and 6-field cron expressions."),
        };
    }
}
