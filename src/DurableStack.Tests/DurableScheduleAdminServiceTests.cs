using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;

namespace DurableStack.Tests;

public sealed class DurableScheduleAdminServiceTests
{
    [Fact]
    public async Task Disable_enable_update_and_run_now_for_recurring_job()
    {
        var store = new InMemoryJobStore();
        var registration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(object),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        var registry = new DurableStackJobRegistry(new[] { registration });
        var initializer = new RecurringJobInitializer(registry, store);
        await initializer.InitializeAsync(CancellationToken.None);

        IDurableScheduleAdminService admin = new DurableScheduleAdminService(store, registry);

        var listBefore = await admin.ListScheduledJobsAsync();
        var before = Assert.Single(listBefore);
        Assert.True(before.Enabled);

        var disabled = await admin.SetScheduledJobEnabledAsync("heartbeat", enabled: false);
        Assert.True(disabled);

        var listDisabled = await admin.ListScheduledJobsAsync();
        Assert.False(Assert.Single(listDisabled).Enabled);

        var cronUpdated = await admin.UpdateScheduledJobCronAsync("heartbeat", "*/5 * * * *", "UTC");
        Assert.True(cronUpdated);

        var listUpdated = await admin.ListScheduledJobsAsync();
        Assert.Equal("*/5 * * * *", Assert.Single(listUpdated).CronExpression);

        var enabled = await admin.SetScheduledJobEnabledAsync("heartbeat", enabled: true);
        Assert.True(enabled);
        Assert.True(Assert.Single(await admin.ListScheduledJobsAsync()).Enabled);

        var queued = await admin.RunScheduledJobNowAsync("heartbeat");
        Assert.True(queued);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs);
        Assert.Null(runs[0].ScheduleSlotUtc);
    }
}
