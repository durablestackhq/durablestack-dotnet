using System;
using Cronos;

namespace DurableStack.Core.Scheduling;

public static class CronScheduleCalculator
{
    public static DateTimeOffset GetNextOccurrenceUtc(string cronExpression, string timeZone, DateTimeOffset fromUtc)
    {
        var expression = CronExpression.Parse(cronExpression, CronFormat.Standard);
        var zone = TimeZoneResolver.ResolveFromIana(timeZone);

        var next = expression.GetNextOccurrence(fromUtc.UtcDateTime, zone, inclusive: false);
        if (next is null)
        {
            throw new InvalidOperationException($"No next cron occurrence was found for expression '{cronExpression}'.");
        }

        return new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc));
    }
}
