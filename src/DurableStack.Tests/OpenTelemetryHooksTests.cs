using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Events;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Hosting.Events;
using DurableStack.Hosting.Hosting;
using DurableStack.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace DurableStack.Tests;

public sealed class OpenTelemetryHooksTests
{
    [Fact]
    public async Task Processor_emits_durable_stack_trace_activity()
    {
        TestNoArgsJob.Executions.Clear();

        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DurableStackTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (activities)
                {
                    activities.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);

        await RunProcessorOnceAsync(new TestNoArgsJob());

        var activity = Assert.Single(activities, x => x.OperationName == "durablestack.job.execute");
        Assert.NotNull(activity.GetTagItem("durablestack.run_id"));
        Assert.Equal("no-args", activity.GetTagItem("durablestack.job_name"));
        Assert.Equal(1, activity.GetTagItem("durablestack.attempt"));
        Assert.Equal("otel-worker", activity.GetTagItem("durablestack.worker_name"));
    }

    [Fact]
    public async Task Processor_emits_durable_stack_metrics_for_job_lifecycle()
    {
        TestNoArgsJob.Executions.Clear();

        var totals = await CaptureLongCounterMetricsAsync(async () =>
        {
            await RunProcessorOnceAsync(new TestNoArgsJob());
        });

        AssertMetricAtLeast(totals, "durablestack.worker.polls", 1);
        AssertMetricAtLeast(totals, "durablestack.jobs.claimed", 1);
        AssertMetricAtLeast(totals, "durablestack.jobs.started", 1);
        AssertMetricAtLeast(totals, "durablestack.jobs.succeeded", 1);
    }

    [Fact]
    public async Task LeaseHeartbeatRunner_emits_lease_extension_metric()
    {
        TestNoArgsJob.Executions.Clear();

        var totals = await CaptureLongCounterMetricsAsync(async () =>
        {
            var store = new InMemoryJobStore();
            var options = new DurableStackOptions
            {
                WorkerName = "otel-worker",
                LeaseDuration = TimeSpan.FromMilliseconds(300),
            };

            var registry = new DurableStackJobRegistry(new[]
            {
                new DurableJobRegistration
                {
                    JobName = "long-running",
                    JobType = typeof(LongRunningNoArgsJob),
                    MaxAttempts = 3,
                },
            });

            using var provider = BuildServiceProvider(new LongRunningNoArgsJob(TimeSpan.FromMilliseconds(900)));
            var baseRunner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
            var runner = new LeaseHeartbeatJobRunner(baseRunner, store, options);

            var runId = await store.EnqueueAsync(
                "long-running",
                typeof(LongRunningNoArgsJob).AssemblyQualifiedName ?? typeof(LongRunningNoArgsJob).FullName ?? typeof(LongRunningNoArgsJob).Name,
                null,
                DateTimeOffset.UtcNow,
                3,
                CancellationToken.None);

            var claimed = await store.ClaimDueRunsAsync(options.WorkerName, 1, options.LeaseDuration, CancellationToken.None);
            var run = Assert.Single(claimed);

            await runner.RunAsync(run, CancellationToken.None);

            var persisted = await store.GetRunAsync(runId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal("leased", persisted!.Status);
        });

        AssertMetricAtLeast(totals, "durablestack.leases.extended", 1);
    }

    [Fact]
    public async Task HostedService_emits_worker_heartbeat_metric()
    {
        var totals = await CaptureLongCounterMetricsAsync(async () =>
        {
            var options = new DurableStackOptions
            {
                WorkerName = "otel-worker",
                PollInterval = TimeSpan.FromSeconds(5),
            };

            var processor = new SignalProcessor();
            var service = new DurableStackHostedService(
                processor,
                new NoOpMigrator(),
                new NoOpRecurringInitializer(),
                options,
                Array.Empty<IDurableStackEventSink>(),
                new DurableStackEventFactory(options),
                NullLogger<DurableStackHostedService>.Instance);

            await service.StartAsync(CancellationToken.None);
            await processor.WaitForFirstCallAsync();
            await service.StopAsync(CancellationToken.None);
        });

        AssertMetricAtLeast(totals, "durablestack.worker.heartbeats", 1);
    }

    [Fact]
    public async Task HostedService_continues_heartbeats_while_processing_is_in_flight()
    {
        var options = new DurableStackOptions
        {
            WorkerName = "otel-worker",
            PollInterval = TimeSpan.FromMilliseconds(50),
        };

        var processor = new BlockingProcessor();
        var sink = new RecordingEventSink();

        var service = new DurableStackHostedService(
            processor,
            new NoOpMigrator(),
            new NoOpRecurringInitializer(),
            options,
            new[] { sink },
            new DurableStackEventFactory(options),
            NullLogger<DurableStackHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await processor.WaitForFirstCallAsync();
        await Task.Delay(220);

        var heartbeatsBeforeRelease = sink.Events.Count(x => x.EventType == DurableStackEventTypes.WorkerHeartbeat);

        processor.Release();
        await service.StopAsync(CancellationToken.None);

        Assert.True(heartbeatsBeforeRelease >= 2, $"Expected at least 2 heartbeats while processing blocked, observed {heartbeatsBeforeRelease}.");
    }

    [Fact]
    public async Task IngestionSync_continues_flushing_while_processing_is_in_flight()
    {
        var options = new DurableStackOptions
        {
            WorkerName = "otel-worker",
            PollInterval = TimeSpan.FromMilliseconds(50),
        };
        options.Eventing.TenantId = "tenant-test";
        options.Eventing.ClientSecret = "secret-test";
        options.Eventing.IngestionApiBaseUrl = "https://example.test";
        options.Eventing.IngestionFlushInterval = TimeSpan.FromMilliseconds(50);
        options.Eventing.IngestionMaxRetryAttempts = 1;

        var processor = new BlockingProcessor();
        var sink = new IngestionDurableStackEventSink(NullLogger<IngestionDurableStackEventSink>.Instance);
        var handler = new CountingHttpMessageHandler();
        var ingestionService = new IngestionEventSyncHostedService(
            new IDurableStackEventSink[] { sink },
            options,
            new FixedHttpClientFactory(new HttpClient(handler)),
            NullLogger<IngestionEventSyncHostedService>.Instance);

        var workerService = new DurableStackHostedService(
            processor,
            new NoOpMigrator(),
            new NoOpRecurringInitializer(),
            options,
            new IDurableStackEventSink[] { sink },
            new DurableStackEventFactory(options),
            NullLogger<DurableStackHostedService>.Instance);

        await ingestionService.StartAsync(CancellationToken.None);
        await workerService.StartAsync(CancellationToken.None);

        await processor.WaitForFirstCallAsync();

        // Poll instead of asserting on a fixed window: a loaded test agent can starve
        // the 50 ms flush loop well past any fixed delay.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (handler.RequestCount < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        var postsWhileBlocked = handler.RequestCount;

        processor.Release();
        await workerService.StopAsync(CancellationToken.None);
        await ingestionService.StopAsync(CancellationToken.None);

        Assert.True(postsWhileBlocked >= 1, $"Expected ingestion flushes while processing blocked, observed {postsWhileBlocked} posts.");
    }

    [Fact]
    public void AddDurableStackOpenTelemetry_registers_without_startup_errors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableStack();

        services.AddDurableStackOpenTelemetry();

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hostedServices, x => x is DurableStackHostedService);
    }

    private static async Task RunProcessorOnceAsync(IDurableJob job)
    {
        var store = new InMemoryJobStore();
        var options = new DurableStackOptions
        {
            WorkerName = "otel-worker",
            ClaimBatchSize = 5,
            LeaseDuration = TimeSpan.FromSeconds(30),
        };

        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "no-args",
                JobType = job.GetType(),
                MaxAttempts = 3,
            },
        });

        using var provider = BuildServiceProvider(job);
        var runner = new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
        var processor = new DurableStackProcessor(
            store,
            registry,
            runner,
            new NoOpRecurringScheduler(),
            options,
            Array.Empty<IDurableStackEventSink>(),
            new DurableStackEventFactory(options));

        await store.EnqueueAsync("no-args", job.GetType().Name, null, DateTimeOffset.UtcNow, 3, CancellationToken.None);
        var processed = await processor.ProcessOnceAsync(CancellationToken.None);
        Assert.Equal(1, processed);
    }

    private static async Task<Dictionary<string, long>> CaptureLongCounterMetricsAsync(Func<Task> action)
    {
        var totals = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DurableStackTelemetry.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            totals.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
        });

        listener.Start();
        await action();

        return totals.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    private static void AssertMetricAtLeast(IReadOnlyDictionary<string, long> totals, string metricName, long minimum)
    {
        Assert.True(totals.TryGetValue(metricName, out var value), $"Metric '{metricName}' was not observed.");
        Assert.True(value >= minimum, $"Metric '{metricName}' expected >= {minimum}, observed {value}.");
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

    private sealed class NoOpRecurringScheduler : IRecurringJobScheduler
    {
        public Task<int> MaterializeDueRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpMigrator : IDurableStackStoreMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpRecurringInitializer : IRecurringJobInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SignalProcessor : IDurableStackProcessor
    {
        private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
        {
            _firstCall.TrySetResult();
            return Task.FromResult(0);
        }

        public async Task WaitForFirstCallAsync()
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(_firstCall.Task, timeout);
            Assert.True(completed == _firstCall.Task, "Hosted service did not process a loop in time.");
        }
    }

    private sealed class BlockingProcessor : IDurableStackProcessor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public async Task WaitForFirstCallAsync()
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(_entered.Task, timeout);
            Assert.True(completed == _entered.Task, "Processing loop did not enter in time.");
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class RecordingEventSink : IDurableStackEventSink
    {
        private readonly object _gate = new();
        private readonly List<DurableStackEvent> _events = new();

        public IReadOnlyList<DurableStackEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToList();
                }
            }
        }

        public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _events.Add(@event);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }
}
