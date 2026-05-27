using System;
using System.Linq;
using System.Threading;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Tests.TestSupport;

namespace DurableStack.Tests;

public sealed class RecurringSchedulingTests
{
    [Fact]
    public async Task Initializer_registers_recurring_job_state_and_scheduler_enqueues_due_run()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "every-minute-job",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
                CronExpression = "* * * * *",
                TimeZone = "UTC",
            },
        });

        var initializer = new RecurringJobInitializer(registry, store);
        await initializer.InitializeAsync(CancellationToken.None);

        // Force job due now for deterministic test.
        await store.UpdateRecurringNextRunAsync("every-minute-job", DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var created = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, created);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("every-minute-job", run.JobName);
        Assert.Equal("pending", run.Status);

        var dueAfterMaterialize = await store.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);
        Assert.DoesNotContain(dueAfterMaterialize, x => x.JobName == "every-minute-job");
    }

    [Fact]
    public async Task Scheduler_materializes_recurring_slot_once_under_concurrency()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "every-minute-job",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
                CronExpression = "* * * * *",
                TimeZone = "UTC",
            },
        });

        var initializer = new RecurringJobInitializer(registry, store);
        await initializer.InitializeAsync(CancellationToken.None);
        await store.UpdateRecurringNextRunAsync("every-minute-job", DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None);

        var schedulerA = new RecurringJobScheduler(store, registry, options);
        var schedulerB = new RecurringJobScheduler(store, registry, options);

        var created = await Task.WhenAll(
            schedulerA.MaterializeDueRunsAsync(CancellationToken.None),
            schedulerB.MaterializeDueRunsAsync(CancellationToken.None));

        Assert.Equal(1, created.Sum());

        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.Single(runs, x => x.JobName == "every-minute-job");
    }

    [Fact]
    public async Task Scheduler_skip_missed_policy_jumps_to_next_future_slot()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                CatchUpPolicy = RecurringCatchUpPolicy.SkipMissed,
            },
        };

        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "every-minute-job",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
                CronExpression = "* * * * *",
                TimeZone = "UTC",
            },
        });

        var initializer = new RecurringJobInitializer(registry, store);
        await initializer.InitializeAsync(CancellationToken.None);

        var pastDue = DateTimeOffset.UtcNow.AddMinutes(-10);
        await store.UpdateRecurringNextRunAsync("every-minute-job", pastDue, CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var created = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, created);

        var dueAfter = await store.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);
        Assert.DoesNotContain(dueAfter, x => x.JobName == "every-minute-job");
    }

    [Fact]
    public async Task Scheduler_catch_up_policy_replays_next_missed_slot()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                CatchUpPolicy = RecurringCatchUpPolicy.CatchUp,
            },
        };

        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "every-minute-job",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
                CronExpression = "* * * * *",
                TimeZone = "UTC",
            },
        });

        var initializer = new RecurringJobInitializer(registry, store);
        await initializer.InitializeAsync(CancellationToken.None);

        var pastDue = DateTimeOffset.UtcNow.AddMinutes(-10);
        await store.UpdateRecurringNextRunAsync("every-minute-job", pastDue, CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var created = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, created);

        var dueAfter = await store.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);
        Assert.Contains(dueAfter, x => x.JobName == "every-minute-job");
    }
}
