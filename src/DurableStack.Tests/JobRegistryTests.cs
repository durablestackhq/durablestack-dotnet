using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Tests.TestSupport;

namespace DurableStack.Tests;

public sealed class JobRegistryTests
{
    [Fact]
    public void Constructor_registers_job_and_can_find_by_name_and_type()
    {
        var registration = new DurableJobRegistration
        {
            JobName = "test-job",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 4,
        };

        var registry = new DurableStackJobRegistry(new[] { registration });

        var byName = registry.FindByName("test-job");
        var byType = registry.FindByJobType(typeof(TestNoArgsJob));

        Assert.NotNull(byName);
        Assert.NotNull(byType);
        Assert.Equal("test-job", byName!.JobName);
        Assert.Equal(typeof(TestNoArgsJob), byType!.JobType);
        Assert.Equal(4, byName.MaxAttempts);
    }

    [Fact]
    public void Constructor_throws_for_duplicate_job_name()
    {
        var first = new DurableJobRegistration
        {
            JobName = "dupe",
            JobType = typeof(TestNoArgsJob),
        };

        var second = new DurableJobRegistration
        {
            JobName = "dupe",
            JobType = typeof(TestArgsJob),
            PayloadType = typeof(TestArgs),
        };

        Assert.Throws<InvalidOperationException>(() => new DurableStackJobRegistry(new[] { first, second }));
    }

    [Fact]
    public void Constructor_throws_for_recurring_job_with_windows_time_zone_id()
    {
        var registration = new DurableJobRegistration
        {
            JobName = "recurring-job",
            JobType = typeof(TestNoArgsJob),
            CronExpression = "* * * * *",
            TimeZone = "Central Standard Time",
        };

        Assert.Throws<ArgumentException>(() => new DurableStackJobRegistry(new[] { registration }));
    }

    [Fact]
    public void Constructor_accepts_recurring_job_with_iana_time_zone_id()
    {
        var registration = new DurableJobRegistration
        {
            JobName = "recurring-job",
            JobType = typeof(TestNoArgsJob),
            CronExpression = "* * * * *",
            TimeZone = "America/Chicago",
        };

        var registry = new DurableStackJobRegistry(new[] { registration });
        var byName = registry.FindByName("recurring-job");

        Assert.NotNull(byName);
        Assert.Equal("America/Chicago", byName!.TimeZone);
    }
}
