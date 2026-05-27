using System;
using TimeZoneConverter;

namespace DurableStack.Core.Scheduling;

public static class TimeZoneResolver
{
    public static TimeZoneInfo ResolveFromIana(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Recurring job time zone must be a non-empty IANA time zone ID.", nameof(timeZoneId));
        }

        var trimmed = timeZoneId.Trim();
        var looksLikeIana = trimmed.Contains('/');

        if (!looksLikeIana && !string.Equals(trimmed, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Recurring job time zone '{trimmed}' is not a supported IANA time zone ID. Use values like 'America/Chicago' or 'UTC'.",
                nameof(timeZoneId));
        }

        try
        {
            return TZConvert.GetTimeZoneInfo(trimmed);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException(
                $"Recurring job time zone '{trimmed}' is not a recognized IANA time zone ID.",
                nameof(timeZoneId),
                ex);
        }
    }
}
