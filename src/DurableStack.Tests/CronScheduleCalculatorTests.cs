using DurableStack.Core.Scheduling;

namespace DurableStack.Tests;

public sealed class CronScheduleCalculatorTests
{
    [Fact]
    public void GetNextOccurrenceUtc_supports_five_field_cron()
    {
        var fromUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var next = CronScheduleCalculator.GetNextOccurrenceUtc("*/5 * * * *", "UTC", fromUtc);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 5, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceUtc_supports_six_field_cron_with_seconds()
    {
        var fromUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 5, TimeSpan.Zero);

        var next = CronScheduleCalculator.GetNextOccurrenceUtc("*/30 * * * * *", "UTC", fromUtc);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceUtc_throws_for_invalid_field_count()
    {
        var fromUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CronScheduleCalculator.GetNextOccurrenceUtc("* * * *", "UTC", fromUtc));

        Assert.Contains("supports 5-field and 6-field cron expressions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
