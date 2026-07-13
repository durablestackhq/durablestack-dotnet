using System;
using TimeZoneConverter;

namespace DurableStack.Core.Scheduling;

/// <summary>
/// Resolves IANA time zone identifiers to <see cref="TimeZoneInfo"/> via TimeZoneConverter,
/// so ids like <c>America/Chicago</c> work on both Windows and Linux hosts.
/// </summary>
public static class TimeZoneResolver
{
    /// <summary>
    /// Resolves an IANA time zone id (for example <c>America/Chicago</c>) or <c>UTC</c>.
    /// Inputs that do not look like IANA ids — anything without a <c>/</c> other than
    /// <c>UTC</c>, including Windows ids — are rejected.
    /// </summary>
    /// <param name="timeZoneId">The IANA time zone identifier to resolve; leading and trailing whitespace is ignored.</param>
    /// <returns>The resolved <see cref="TimeZoneInfo"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The id is empty, is not IANA-shaped, or is not a recognized time zone.
    /// </exception>
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
