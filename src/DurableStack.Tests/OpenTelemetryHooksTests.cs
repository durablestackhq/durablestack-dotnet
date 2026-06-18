using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Events;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Hosting.Hosting;
using DurableStack.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

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
            BatchSize = 5,
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
}
