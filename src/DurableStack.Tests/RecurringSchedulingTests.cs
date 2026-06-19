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

        var initializer = new RecurringJobInitializer(registry, store, options);
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

        var initializer = new RecurringJobInitializer(registry, store, options);
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

        var initializer = new RecurringJobInitializer(registry, store, options);
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

        var initializer = new RecurringJobInitializer(registry, store, options);
        await initializer.InitializeAsync(CancellationToken.None);

        var pastDue = DateTimeOffset.UtcNow.AddMinutes(-10);
        await store.UpdateRecurringNextRunAsync("every-minute-job", pastDue, CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var created = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, created);

        var dueAfter = await store.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);
        Assert.Contains(dueAfter, x => x.JobName == "every-minute-job");
    }

    [Fact]
    public async Task Scheduler_skips_materialization_when_active_run_exists_and_concurrency_disabled()
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

        var initializer = new RecurringJobInitializer(registry, store, options);
        await initializer.InitializeAsync(CancellationToken.None);
        await store.UpdateRecurringNextRunAsync("every-minute-job", DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var createdFirst = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);
        var createdSecond = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, createdFirst);
        Assert.Equal(0, createdSecond);

        var runs = await store.GetRunsByJobNameAsync("every-minute-job", 10, CancellationToken.None);
        Assert.Single(runs);
    }

    [Fact]
    public async Task Scheduler_materializes_multiple_when_only_leased_run_exists_and_concurrency_enabled()
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
                AllowConcurrentRuns = true,
            },
        });

        var initializer = new RecurringJobInitializer(registry, store, options);
        await initializer.InitializeAsync(CancellationToken.None);
        await store.UpdateRecurringNextRunAsync("every-minute-job", DateTimeOffset.UtcNow.AddMinutes(-10), CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var createdFirst = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);
        var claimed = await store.ClaimDueRunsAsync("worker-a", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(claimed);
        var createdSecond = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, createdFirst);
        Assert.Equal(1, createdSecond);

        var runs = await store.GetRunsByJobNameAsync("every-minute-job", 10, CancellationToken.None);
        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public async Task Scheduler_does_not_materialize_second_run_when_pending_exists_and_concurrency_enabled()
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
                AllowConcurrentRuns = true,
            },
        });

        var initializer = new RecurringJobInitializer(registry, store, options);
        await initializer.InitializeAsync(CancellationToken.None);
        await store.UpdateRecurringNextRunAsync("every-minute-job", DateTimeOffset.UtcNow.AddMinutes(-10), CancellationToken.None);

        var scheduler = new RecurringJobScheduler(store, registry, options);
        var createdFirst = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);
        var createdSecond = await scheduler.MaterializeDueRunsAsync(CancellationToken.None);

        Assert.Equal(1, createdFirst);
        Assert.Equal(0, createdSecond);

        var runs = await store.GetRunsByJobNameAsync("every-minute-job", 10, CancellationToken.None);
        Assert.Single(runs);
        Assert.Equal("pending", runs[0].Status);
    }

    [Fact]
    public async Task Initializer_keep_database_behavior_preserves_existing_schedule()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                RegistrationSync =
                {
                    ExistingJobBehavior = ExistingRecurringJobBehavior.KeepDatabase,
                },
            },
        };

        var existingRegistration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(existingRegistration, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);
        _ = await store.SetRecurringJobEnabledAsync("heartbeat", enabled: false, nextRunAtUtc: null, CancellationToken.None);

        var updatedCodeRegistration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 5,
            CronExpression = "*/5 * * * *",
            TimeZone = "America/Chicago",
        };

        var registry = new DurableStackJobRegistry(new[] { updatedCodeRegistration });
        await new RecurringJobInitializer(registry, store, options).InitializeAsync(CancellationToken.None);

        var schedule = Assert.Single(await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None));
        Assert.Equal("* * * * *", schedule.CronExpression);
        Assert.Equal("UTC", schedule.TimeZone);
        Assert.False(schedule.Enabled);
    }

    [Fact]
    public async Task Initializer_update_from_code_behavior_overwrites_existing_schedule()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                RegistrationSync =
                {
                    ExistingJobBehavior = ExistingRecurringJobBehavior.UpdateFromCode,
                },
            },
        };

        var existingRegistration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(existingRegistration, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        var updatedCodeRegistration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 5,
            CronExpression = "*/5 * * * *",
            TimeZone = "America/Chicago",
        };

        var registry = new DurableStackJobRegistry(new[] { updatedCodeRegistration });
        await new RecurringJobInitializer(registry, store, options).InitializeAsync(CancellationToken.None);

        var schedule = Assert.Single(await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None));
        Assert.Equal("*/5 * * * *", schedule.CronExpression);
        Assert.Equal("America/Chicago", schedule.TimeZone);
        Assert.True(schedule.Enabled);
        Assert.Equal(5, schedule.MaxAttempts);
    }

    [Fact]
    public async Task Initializer_disables_orphaned_recurring_jobs_by_default()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions();

        var orphanRegistration = new DurableJobRegistration
        {
            JobName = "orphaned-job",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(orphanRegistration, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        var registry = new DurableStackJobRegistry(Array.Empty<DurableJobRegistration>());
        await new RecurringJobInitializer(registry, store, options).InitializeAsync(CancellationToken.None);

        var schedule = Assert.Single(await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None));
        Assert.False(schedule.Enabled);
    }

    [Fact]
    public async Task Initializer_can_ignore_orphaned_recurring_jobs()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                RegistrationSync =
                {
                    OrphanedJobBehavior = OrphanedRecurringJobBehavior.Ignore,
                },
            },
        };

        var orphanRegistration = new DurableJobRegistration
        {
            JobName = "orphaned-job",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(orphanRegistration, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        var registry = new DurableStackJobRegistry(Array.Empty<DurableJobRegistration>());
        await new RecurringJobInitializer(registry, store, options).InitializeAsync(CancellationToken.None);

        var schedule = Assert.Single(await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None));
        Assert.True(schedule.Enabled);
    }

    [Fact]
    public async Task Initializer_respects_registration_enabled_false_on_first_insert()
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            Recurring =
            {
                RegistrationSync =
                {
                    ExistingJobBehavior = ExistingRecurringJobBehavior.UpdateFromCode,
                },
            },
        };

        var disabledRegistration = new DurableJobRegistration
        {
            JobName = "disabled-job",
            JobType = typeof(TestNoArgsJob),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
            Enabled = false,
        };

        var registry = new DurableStackJobRegistry(new[] { disabledRegistration });
        await new RecurringJobInitializer(registry, store, options).InitializeAsync(CancellationToken.None);

        var schedule = Assert.Single(await store.GetRecurringJobsAsync(includeDisabled: true, CancellationToken.None));
        Assert.False(schedule.Enabled);
    }
}

