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
        var disabledSchedule = Assert.Single(listDisabled);
        Assert.False(disabledSchedule.Enabled);

        var cronUpdated = await admin.UpdateScheduledJobCronAsync("heartbeat", "*/5 * * * *", "UTC");
        Assert.True(cronUpdated);

        var listUpdated = await admin.ListScheduledJobsAsync();
        Assert.Equal("*/5 * * * *", Assert.Single(listUpdated).CronExpression);

        var enabled = await admin.SetScheduledJobEnabledAsync("heartbeat", enabled: true);
        Assert.True(enabled);
        var enabledSchedule = Assert.Single(await admin.ListScheduledJobsAsync());
        Assert.True(enabledSchedule.Enabled);
        Assert.True(enabledSchedule.NextRunAtUtc > DateTimeOffset.UtcNow);

        var runId = await admin.RunScheduledJobNowAsync("heartbeat");
        Assert.NotNull(runId);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs);
        Assert.Equal(runId, runs[0].Id);
        Assert.Null(runs[0].ScheduleSlotUtc);
    }

    [Fact]
    public async Task UpdateScheduledJobCronAsync_recomputes_next_run()
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
        await new RecurringJobInitializer(registry, store).InitializeAsync(CancellationToken.None);

        IDurableScheduleAdminService admin = new DurableScheduleAdminService(store, registry);

        var updated = await admin.UpdateScheduledJobCronAsync("heartbeat", "*/5 * * * *", "UTC");
        Assert.True(updated);

        var schedule = Assert.Single(await admin.ListScheduledJobsAsync());
        Assert.Equal("*/5 * * * *", schedule.CronExpression);
        var now = DateTimeOffset.UtcNow;
        Assert.True(schedule.NextRunAtUtc > now);
        Assert.True(schedule.NextRunAtUtc <= now.AddMinutes(6));
    }

    [Fact]
    public async Task RunScheduledJobNowAsync_does_not_modify_schedule_definition()
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
        await new RecurringJobInitializer(registry, store).InitializeAsync(CancellationToken.None);

        IDurableScheduleAdminService admin = new DurableScheduleAdminService(store, registry);
        var before = Assert.Single(await admin.ListScheduledJobsAsync());

        var runId = await admin.RunScheduledJobNowAsync("heartbeat");
        Assert.NotNull(runId);

        var after = Assert.Single(await admin.ListScheduledJobsAsync());
        Assert.Equal(before.CronExpression, after.CronExpression);
        Assert.Equal(before.TimeZone, after.TimeZone);
        Assert.Equal(before.Enabled, after.Enabled);
    }

    [Fact]
    public async Task RunScheduledJobNowAsync_returns_null_for_unknown_job()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(Array.Empty<DurableJobRegistration>());

        IDurableScheduleAdminService admin = new DurableScheduleAdminService(store, registry);
        var runId = await admin.RunScheduledJobNowAsync("missing-job");

        Assert.Null(runId);
    }
}
