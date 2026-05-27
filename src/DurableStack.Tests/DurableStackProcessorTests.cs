using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Tests.TestSupport;

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

        var provider = new InlineServiceProvider(new TestNoArgsJob());
        var runner = new DefaultDurableJobRunner(provider, registry);
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("no-args", typeof(TestNoArgsJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processor = new DurableStackProcessor(store, runner, scheduler, options, new[] { events }, eventFactory);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(TestNoArgsJob.Executions);

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
        Assert.All(events.Events, e => Assert.Equal("test", e.Environment));
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

        var provider = new InlineServiceProvider(new AlwaysFailJob());
        var runner = new DefaultDurableJobRunner(provider, registry);
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromSeconds(30),
        };
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("always-fail", typeof(AlwaysFailJob).Name, null, DateTimeOffset.UtcNow, 2, CancellationToken.None);

        var processor = new DurableStackProcessor(store, runner, scheduler, options, new[] { events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);

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
        Assert.NotNull(failedEvent.ErrorDetail);
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

        var provider = new InlineServiceProvider(new AlwaysFailJob());
        var runner = new DefaultDurableJobRunner(provider, registry);
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromSeconds(30),
        };
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("always-fail", typeof(AlwaysFailJob).Name, null, DateTimeOffset.UtcNow, 1, CancellationToken.None);

        var processor = new DurableStackProcessor(store, runner, scheduler, options, new[] { events }, eventFactory);
        await processor.ProcessOnceAsync(CancellationToken.None);

        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("failed", run.Status);
        Assert.Equal(1, run.Attempt);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Contains(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.JobRetried);
        Assert.DoesNotContain(events.Events, e => e.EventType == DurableStackEventTypes.RetryScheduled);
        var failedEvent = Assert.Single(events.Events, e => e.EventType == DurableStackEventTypes.JobFailed);
        Assert.NotNull(failedEvent.ErrorType);
        Assert.NotNull(failedEvent.ErrorDetail);
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

        var provider = new InlineServiceProvider(new TestNoArgsJob());
        var runner = new DefaultDurableJobRunner(provider, registry);
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var events = new RecordingEventSink();
        var eventFactory = BuildEventFactory(options);
        var scheduler = new NoOpRecurringJobScheduler();

        var runId = await store.EnqueueAsync("no-args", typeof(TestNoArgsJob).Name, null, DateTimeOffset.UtcNow.AddSeconds(-5), 3, CancellationToken.None);
        var leased = await store.ClaimDueRunsAsync("stale-worker", 1, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Single(leased);

        await Task.Delay(20);

        var processor = new DurableStackProcessor(store, runner, scheduler, options, new[] { events }, eventFactory);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Single(TestNoArgsJob.Executions);

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
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromMilliseconds(300),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };

        var provider = new InlineServiceProvider(new LongRunningNoArgsJob(TimeSpan.FromMilliseconds(900)));
        var baseRunner = new DefaultDurableJobRunner(provider, registry);
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

        var processor = new DurableStackProcessor(store, runner, scheduler, options, new[] { events }, eventFactory);

        var processingTask = processor.ProcessOnceAsync(CancellationToken.None);
        await Task.Delay(500);

        var inFlight = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(inFlight);
        Assert.Equal("leased", inFlight!.Status);
        Assert.NotNull(inFlight.LeaseUntilUtc);
        Assert.True(inFlight.LeaseUntilUtc > DateTimeOffset.UtcNow);

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
        var provider = new InlineServiceProvider(job);
        var baseRunner = new DefaultDurableJobRunner(provider, registry);
        var optionsA = new DurableStackOptions
        {
            WorkerName = "worker-a",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        var optionsB = new DurableStackOptions
        {
            WorkerName = "worker-b",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };

        var runnerA = new LeaseHeartbeatJobRunner(baseRunner, store, optionsA);
        var runnerB = new LeaseHeartbeatJobRunner(baseRunner, store, optionsB);
        var events = new RecordingEventSink();
        var eventFactoryA = BuildEventFactory(optionsA);
        var eventFactoryB = BuildEventFactory(optionsB);
        var scheduler = new NoOpRecurringJobScheduler();

        await store.EnqueueAsync("atomic-counter", typeof(AtomicCounterJob).Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);

        var processorA = new DurableStackProcessor(store, runnerA, scheduler, optionsA, new[] { events }, eventFactoryA);
        var processorB = new DurableStackProcessor(store, runnerB, scheduler, optionsB, new[] { events }, eventFactoryB);

        var processed = await Task.WhenAll(
            processorA.ProcessOnceAsync(CancellationToken.None),
            processorB.ProcessOnceAsync(CancellationToken.None));

        Assert.Equal(1, processed.Sum());
        Assert.Equal(1, AtomicCounterJob.ExecutionCount);

        var run = Assert.Single(await store.GetRunsAsync(CancellationToken.None));
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(1, run.Attempt);
    }

    private static DurableStackEventFactory BuildEventFactory(DurableStackOptions options)
    {
        options.Eventing.TenantId = "tenant-alpha";
        options.Eventing.Environment = "test";
        options.Eventing.ServiceName = "durable-stack-tests";
        return new DurableStackEventFactory(options);
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
}
