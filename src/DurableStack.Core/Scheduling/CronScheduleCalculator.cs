using System;
using Cronos;

namespace DurableStack.Core.Scheduling;

public static class CronScheduleCalculator
{
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
