using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Tests;

public sealed class SchedulerResilienceTests
{
    [Fact]
    public async Task MaterializeDueRunsAsync_isolates_bad_schedule_rows()
    {
        var store = new InMemoryJobStore();
        var goodRegistration = new DurableJobRegistration
        {
            JobName = "good-job",
            JobType = typeof(object),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };
        var badRegistration = new DurableJobRegistration
        {
            JobName = "bad-job",
            JobType = typeof(string),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };
        var registry = new DurableStackJobRegistry(new[] { goodRegistration, badRegistration });

        var due = DateTimeOffset.UtcNow.AddSeconds(-1);
        await store.UpsertRecurringJobAsync(goodRegistration, due, CancellationToken.None);
        await store.UpsertRecurringJobAsync(badRegistration, due, CancellationToken.None);

        // Corrupt the stored schedule (simulating a row written by an older code
        // version): a cron expression with no next occurrence throws during
        // materialization, which previously aborted every schedule after it.
        Assert.True(await store.UpdateRecurringJobScheduleAsync("bad-job", "0 0 30 2 *", "UTC", due, CancellationToken.None));

        var scheduler = new RecurringJobScheduler(store, registry, new DurableStackOptions());
        var created = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, created);
        var runs = await store.GetRunsAsync(CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("good-job", run.JobName);
    }

    [Fact]
    public void Registering_recurring_job_with_invalid_cron_fails_fast()
    {
        Assert.ThrowsAny<Exception>(() => new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "broken",
                JobType = typeof(object),
                MaxAttempts = 3,
                CronExpression = "banana",
                TimeZone = "UTC",
            },
        }));
    }

    [Fact]
    public void Registering_recurring_job_with_no_next_occurrence_fails_fast()
    {
        Assert.ThrowsAny<Exception>(() => new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "dead-end",
                JobType = typeof(object),
                MaxAttempts = 3,
                CronExpression = "0 0 30 2 *",
                TimeZone = "UTC",
            },
        }));
    }
}
