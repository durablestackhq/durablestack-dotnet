using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableStackProcessorTests
{
    [Fact]
    public async Task ProcessOnceAsync_executes_due_job_and_marks_succeeded()
    {
        TestNoArgsJob.Executions.Clear();

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "no-args",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        using var provider = BuildServiceProvider(new TestNoArgsJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("no-args", typeof(TestNoArgsJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(TestNoArgsJob.Executions, c => c.RunId == runId);

        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobClaimed);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobStarted);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobSucceeded);
        var succeededEvent = Assert.Single(events.Events, e => e.EventType == DurableStackEventTypes.JobSucceeded);
        Assert.NotNull(succeededEvent.DurationMs);
        Assert.True(succeededEvent.DurationMs >= 0);
        Assert.All(events.Events, e => Assert.Equal(DurableStackEventTypes.CurrentVersion, e.EventVersion));
        Assert.All(events.Events, e => Assert.NotEqual(Guid.Empty, e.EventId));
        Assert.All(events.Events, e => Assert.Equal("tenant-alpha", e.TenantId));
        Assert.All(events.Events, e => Assert.Equal("durable-stack-tests", e.ServiceName));
    }

    [Fact]
    public async Task ProcessOnceAsync_marks_failed_run_for_retry_when_attempts_remain()
    {
        AlwaysFailJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "always-fail",
                JobType = typeof(AlwaysFailJob),
                MaxAttempts = 2,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromSeconds(30),
        };
        using var provider = BuildServiceProvider(new AlwaysFailJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("always-fail", typeof(AlwaysFailJob).Name, null, DateTimeOffset.UtcNow, 2, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        Assert.Equal(1, AlwaysFailJob.ExecutionCount);
        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("pending", run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.NotNull(run.ErrorMessage);
        Assert.True(run.ScheduledForUtc > DateTimeOffset.UtcNow.AddSeconds(20));
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobRetried);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.RetryScheduled);
        var failedEvent = Assert.Single(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.NotNull(failedEvent.ErrorType);
        Assert.Null(failedEvent.ErrorMessage);
        Assert.Null(failedEvent.ErrorDetail);
        Assert.NotNull(failedEvent.RetryAtUtc);
        var retryEvent = Assert.Single(events.Events, e => e.EventType == DurableStackEventTypes.RetryScheduled);
        Assert.NotNull(retryEvent.RetryAtUtc);
    }

    [Fact]
    public async Task ProcessOnceAsync_marks_failed_without_retry_when_attempts_exhausted()
    {
        AlwaysFailJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "always-fail",
                JobType = typeof(AlwaysFailJob),
                MaxAttempts = 1,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromSeconds(30),
        };
        using var provider = BuildServiceProvider(new AlwaysFailJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("always-fail", typeof(AlwaysFailJob).Name, null, DateTimeOffset.UtcNow, 1, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("failed", run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobRetried);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.RetryScheduled);
        var failedEvent = Assert.Single(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.NotNull(failedEvent.ErrorType);
        Assert.Null(failedEvent.ErrorMessage);
        Assert.Null(failedEvent.ErrorDetail);
        Assert.Null(failedEvent.RetryAtUtc);
    }

    [Fact]
    public async Task ProcessOnceAsync_reclaims_expired_lease_and_executes_run()
    {
        TestNoArgsJob.Executions.Clear();

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "no-args",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        using var provider = BuildServiceProvider(new TestNoArgsJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("no-args", typeof(TestNoArgsJob).Name, null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);
        var leased = await store.ClaimDueRunsAsync("stale-worker", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Single(leased);

        await Task.Delay(20);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(TestNoArgsJob.Executions, c => c.RunId == runId && c.Attempt == 2);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("succeeded", run!.Status);
        Assert.Equal(2, run.Attempt);
    }

    [Fact]
    public async Task ProcessOnceAsync_extends_lease_for_long_running_job()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "long-running",
                JobType = typeof(LongRunningNoArgsJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromMilliseconds(300),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };

        using var provider = BuildServiceProvider(new LongRunningNoArgsJob(TimeSpan.FromMilliseconds(900)));
        var baseRunner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var runner = new LeaseHeartbeatJobRunner(baseRunner, store, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync(
            "long-running",
            typeof(LongRunningNoArgsJob).AssemblyQualifiedName ?? typeof(LongRunningNoArgsJob).FullName ?? typeof(LongRunningNoArgsJob).Name,
            null,
            DateTimeOffset.UtcNow,
            3,
            CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);

        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await Task.Delay(300);

        var firstSnapshot = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(firstSnapshot);
        Assert.Equal("leased", firstSnapshot!.Status);
        Assert.NotNull(firstSnapshot.LeaseUntilUtc);

        await Task.Delay(250);

        var secondSnapshot = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(secondSnapshot);
        Assert.Equal("leased", secondSnapshot!.Status);
        Assert.NotNull(secondSnapshot.LeaseUntilUtc);
        Assert.True(
            secondSnapshot.LeaseUntilUtc >= firstSnapshot.LeaseUntilUtc,
            $"Expected lease heartbeat to maintain/extend lease window. First={firstSnapshot.LeaseUntilUtc:O}, Second={secondSnapshot.LeaseUntilUtc:O}");

        await processingTask;
    }

    [Fact]
    public async Task ProcessOnceAsync_parallel_workers_execute_run_effectively_once()
    {
        AtomicCounterJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "atomic-counter",
                JobType = typeof(AtomicCounterJob),
                MaxAttempts = 3,
            },
        });

        var job = new AtomicCounterJob();
        var optionsA = new DurableStackOptions
        {
            WorkerName = "worker-a",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var optionsB = new DurableStackOptions
        {
            WorkerName = "worker-b",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };

        using var provider = BuildServiceProvider(job);
        var baseRunner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, optionsA);

        var runnerA = new LeaseHeartbeatJobRunner(baseRunner, store, optionsA);
        var runnerB = new LeaseHeartbeatJobRunner(baseRunner, store, optionsB);
        var events = new RecordingEventSink();
        var eventFactoryA = BuildEventFactory(optionsA);
        var eventFactoryB = BuildEventFactory(optionsB);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("atomic-counter", typeof(AtomicCounterJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processorA = new DurableStackProcessor(store, registry, runnerA, scheduler, optionsA, new[] { events }, eventFactoryA);
        var processorB = new DurableStackProcessor(store, registry, runnerB, scheduler, optionsB, new[] { events }, eventFactoryB);

        var processed = await Task.WhenAll(
            processorA.ProcessOnceAsync(CancellationToken.None),
            processorB.ProcessOnceAsync(CancellationToken.None));

        // ProcessOnceAsync schedules run execution asynchronously after claim.
        // Drain in-flight runs so terminal status assertions are deterministic.
        await Task.WhenAll(
            processorA.DrainInFlightRunsAsync(CancellationToken.None),
            processorB.DrainInFlightRunsAsync(CancellationToken.None));

        Assert.Equal(1, processed.Sum());
        Assert.Equal(1, AtomicCounterJob.ExecutionCount);

        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(1, run.Attempt);
    }

    [Fact]
    public async Task ProcessOnceAsync_uses_backoff_retry_policy_with_job_initial_delay()
    {
        AlwaysFailJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "always-fail",
                JobType = typeof(AlwaysFailJob),
                MaxAttempts = 4,
                RetryBehavior = RetryBehavior.Backoff,
                RetryInitialDelaySeconds = 1,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };

        using var provider = BuildServiceProvider(new AlwaysFailJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("always-fail", typeof(AlwaysFailJob).Name, null, DateTimeOffset.UtcNow, 4, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        var beforeFirst = DateTimeOffset.UtcNow;
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);
        var firstRetryAt = (await store.GetRunsAsync(CancellationToken.None)).Single().ScheduledForUtc;

        await Task.Delay(1200);
        var beforeSecond = DateTimeOffset.UtcNow;
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);
        var secondRetryAt = (await store.GetRunsAsync(CancellationToken.None)).Single().ScheduledForUtc;

        var firstDelay = firstRetryAt - beforeFirst;
        var secondDelay = secondRetryAt - beforeSecond;

        Assert.True(firstDelay.TotalSeconds >= 0.8);
        Assert.True(secondDelay.TotalSeconds >= 1.8);
    }

    [Fact]
    public async Task ProcessOnceAsync_sink_failure_on_success_event_does_not_mark_run_failed()
    {
        TestNoArgsJob.Executions.Clear();

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "no-args",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        using var provider = BuildServiceProvider(new TestNoArgsJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var throwingSink = new ThrowingEventSink(DurableStackEventTypes.JobSucceeded);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("no-args", typeof(TestNoArgsJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        // The throwing sink comes first so a failure there must not skip later sinks.
        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new IDurableStackEventSink[] { throwingSink, events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        Assert.Single(TestNoArgsJob.Executions, c => c.RunId == runId);
        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.Null(run.ErrorMessage);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobSucceeded);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
    }

    [Fact]
    public async Task ProcessOnceAsync_sink_failure_on_claimed_event_still_executes_all_claimed_runs()
    {
        AtomicCounterJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "atomic-counter",
                JobType = typeof(AtomicCounterJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        using var provider = BuildServiceProvider(new AtomicCounterJob());
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var throwingSink = new ThrowingEventSink(DurableStackEventTypes.JobClaimed);
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        for (var i = 0; i < 3; i++)
        {
            await store.EnqueueAsync("atomic-counter", typeof(AtomicCounterJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);
        }

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new IDurableStackEventSink[] { throwingSink }, eventFactory);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        Assert.Equal(3, processed);
        Assert.Equal(3, AtomicCounterJob.ExecutionCount);
        var runs = await store.GetRunsAsync(CancellationToken.None);
        Assert.All(runs, run => Assert.Equal("succeeded", run.Status));
    }

    [Fact]
    public async Task ProcessOnceAsync_shutdown_cancellation_leaves_run_leased_without_recording_failure()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "block-until-cancelled",
                JobType = typeof(BlockUntilCancelledJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var job = new BlockUntilCancelledJob();
        using var provider = BuildServiceProvider(job);
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("block-until-cancelled", typeof(BlockUntilCancelledJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        using var cts = new CancellationTokenSource();
        await processor.ProcessOnceAsync(cts.Token);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("leased", run!.Status);
        Assert.Equal(1, run.Attempt);
        Assert.Null(run.ErrorMessage);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobRetried);
    }

    [Fact]
    public async Task ProcessOnceAsync_fenced_success_write_does_not_overwrite_reclaimed_run()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "block-until-released",
                JobType = typeof(BlockUntilReleasedJob),
                MaxAttempts = 3,
            },
        });

        var options = new DurableStackOptions
        {
            WorkerName = "zombie-worker",
            ClaimBatchSize = 10,
            LeaseDuration = TimeSpan.FromMilliseconds(1),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var job = new BlockUntilReleasedJob();
        using var provider = BuildServiceProvider(job);
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("block-until-released", typeof(BlockUntilReleasedJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processor = new DurableStackProcessor(store, registry, runner, scheduler, options, new[] { events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The 1 ms lease has lapsed while the job is still executing; another worker reclaims the run.
        await Task.Delay(20);
        var stolen = await store.ClaimDueRunsAsync("thief-worker", 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Single(stolen);

        // The zombie worker finishes, but its fenced success write must not apply.
        job.Release.TrySetResult();
        await processor.DrainInFlightRunsAsync(CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);
        Assert.Equal("leased", run!.Status);
        Assert.Equal("thief-worker", run.LeaseOwner);
        Assert.Equal(2, run.Attempt);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobSucceeded);
    }

    private static DurableStackEventFactory BuildEventFactory(DurableStackOptions options)
    {
        options.Eventing.TenantId = "tenant-alpha";
        options.Eventing.ServiceName = "durable-stack-tests";
        return new DurableStackEventFactory(options);
    }

    private static ServiceProvider BuildServiceProvider(params object[] jobs)
    {
        var services = new ServiceCollection();
        foreach (var job in jobs)
        {
            services.AddSingleton(job.GetType(), job);
        }

        return services.BuildServiceProvider();
    }

    private sealed class RecordingEventSink : IDurableStackEventSink
    {
        public System.Collections.Generic.List<DurableStackEvent> Events { get; } = new();

        public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpRecurringJobScheduler : IRecurringJobScheduler
    {
        public Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class ThrowingEventSink : IDurableStackEventSink
    {
        private readonly string _eventTypeToThrowOn;

        public ThrowingEventSink(string eventTypeToThrowOn)
        {
            _eventTypeToThrowOn = eventTypeToThrowOn;
        }

        public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event.EventType == _eventTypeToThrowOn)
            {
                throw new InvalidOperationException($"sink failure on {_eventTypeToThrowOn}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockUntilCancelledJob : IDurableJob
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class BlockUntilReleasedJob : IDurableJob
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
