using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableJobActivationScopeTests
{
    [Fact]
    public async Task Scoped_per_execution_mode_resolves_scoped_dependencies()
    {
        ScopedDependencyJob.ExecutionScopeIds.Clear();

        using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopedDependency>();
            services.AddTransient<ScopedDependencyJob>();
        });

        var registry = CreateRegistry(typeof(ScopedDependencyJob), jobName: "scoped-job");
        var options = new DurableStackOptions();
        var runner = CreateRunner(provider, registry, options);

        await RunSingleAsync(runner, "scoped-job", typeof(ScopedDependencyJob), maxAttempts: 3);

        Assert.Single(ScopedDependencyJob.ExecutionScopeIds);
    }

    [Fact]
    public async Task Scoped_per_execution_mode_uses_different_scoped_instance_per_run()
    {
        ScopedDependencyJob.ExecutionScopeIds.Clear();

        using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopedDependency>();
            services.AddTransient<ScopedDependencyJob>();
        });

        var registry = CreateRegistry(typeof(ScopedDependencyJob), jobName: "scoped-job");
        var options = new DurableStackOptions();
        var runner = CreateRunner(provider, registry, options);

        await RunSingleAsync(runner, "scoped-job", typeof(ScopedDependencyJob), maxAttempts: 3);
        await RunSingleAsync(runner, "scoped-job", typeof(ScopedDependencyJob), maxAttempts: 3);

        Assert.Equal(2, ScopedDependencyJob.ExecutionScopeIds.Count);
        Assert.Equal(2, ScopedDependencyJob.ExecutionScopeIds.Distinct().Count());
    }

    [Fact]
    public async Task Scoped_disposable_dependency_is_disposed_after_successful_run()
    {
        DisposableProbe.Reset();

        using var provider = BuildProvider(services =>
        {
            services.AddScoped<DisposableProbe>();
            services.AddTransient<DisposableProbeJob>();
        });

        var registry = CreateRegistry(typeof(DisposableProbeJob), jobName: "dispose-job");
        var options = new DurableStackOptions();
        var runner = CreateRunner(provider, registry, options);

        await RunSingleAsync(runner, "dispose-job", typeof(DisposableProbeJob), maxAttempts: 3);

        Assert.Equal(1, DisposableProbe.CreatedCount);
        Assert.Equal(1, DisposableProbe.DisposedCount);
    }

    [Fact]
    public async Task Scoped_disposable_dependency_is_disposed_when_job_fails()
    {
        DisposableProbe.Reset();

        using var provider = BuildProvider(services =>
        {
            services.AddScoped<DisposableProbe>();
            services.AddTransient<FailingDisposableProbeJob>();
        });

        var registry = CreateRegistry(typeof(FailingDisposableProbeJob), jobName: "failing-dispose-job");
        var options = new DurableStackOptions();
        var runner = CreateRunner(provider, registry, options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSingleAsync(runner, "failing-dispose-job", typeof(FailingDisposableProbeJob), maxAttempts: 3));

        Assert.Contains("boom", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, DisposableProbe.CreatedCount);
        Assert.Equal(1, DisposableProbe.DisposedCount);
    }

    [Fact]
    public async Task Retry_attempts_use_fresh_scope_instances()
    {
        RetryScopedDependencyJob.ExecutionScopeIds.Clear();
        RetryScopedDependencyJob.ExecutionCount = 0;

        var store = new InMemoryJobStore();
        using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopedDependency>();
            services.AddTransient<RetryScopedDependencyJob>();
        });

        var registry = CreateRegistry(typeof(RetryScopedDependencyJob), jobName: "retry-job", maxAttempts: 2);
        var options = new DurableStackOptions
        {
            WorkerName = "retry-worker",
            BatchSize = 10,
            LeaseDuration = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(10),
        };

        var runner = CreateRunner(provider, registry, options);
        var scheduler = new NoOpRecurringJobScheduler();
        var processor = new DurableStackProcessor(
            store,
            runner,
            scheduler,
            options,
            Array.Empty<IDurableStackEventSink>(),
            new DurableStackEventFactory(options));

        await store.EnqueueAsync("retry-job", typeof(RetryScopedDependencyJob).Name, null, DateTimeOffset.UtcNow, 2, CancellationToken.None);

        _ = await processor.ProcessOnceAsync(CancellationToken.None);
        await Task.Delay(30);
        _ = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2, RetryScopedDependencyJob.ExecutionScopeIds.Count);
        Assert.Equal(2, RetryScopedDependencyJob.ExecutionScopeIds.Distinct().Count());
    }

    [Fact]
    public async Task Root_provider_mode_wraps_scoped_activation_failures_with_guidance()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopedDependency>();
            services.AddTransient<ScopedDependencyJob>();
        });

        var registry = CreateRegistry(typeof(ScopedDependencyJob), jobName: "root-mode-job");
        var options = new DurableStackOptions
        {
            JobActivation = DurableStackJobActivationMode.RootProvider,
        };
        var runner = CreateRunner(provider, registry, options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunSingleAsync(runner, "root-mode-job", typeof(ScopedDependencyJob), maxAttempts: 3));

        Assert.Contains("root activation mode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scoped-per-execution", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    private static DefaultDurableJobRunner CreateRunner(
        ServiceProvider provider,
        IDurableJobRegistry registry,
        DurableStackOptions options)
    {
        return new DefaultDurableJobRunner(provider, provider.GetRequiredService<IServiceScopeFactory>(), registry, options);
    }

    private static DurableStackJobRegistry CreateRegistry(Type jobType, string jobName, int maxAttempts = 3)
    {
        return new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = jobName,
                JobType = jobType,
                MaxAttempts = maxAttempts,
            },
        });
    }

    private static async Task RunSingleAsync(DefaultDurableJobRunner runner, string jobName, Type jobType, int maxAttempts)
    {
        var store = new InMemoryJobStore();
        var runId = await store.EnqueueAsync(
            jobName,
            jobType.AssemblyQualifiedName ?? jobType.FullName ?? jobType.Name,
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts,
            CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        Assert.NotNull(run);

        await runner.RunAsync(run!, CancellationToken.None);
    }

    private sealed class ScopedDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ScopedDependencyJob : IDurableJob
    {
        public static ConcurrentBag<Guid> ExecutionScopeIds { get; } = new();

        private readonly ScopedDependency _dependency;

        public ScopedDependencyJob(ScopedDependency dependency)
        {
            _dependency = dependency;
        }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            ExecutionScopeIds.Add(_dependency.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class RetryScopedDependencyJob : IDurableJob
    {
        public static ConcurrentBag<Guid> ExecutionScopeIds { get; } = new();
        public static int ExecutionCount;

        private readonly ScopedDependency _dependency;

        public RetryScopedDependencyJob(ScopedDependency dependency)
        {
            _dependency = dependency;
        }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            ExecutionScopeIds.Add(_dependency.Id);

            if (Interlocked.Increment(ref ExecutionCount) == 1)
            {
                throw new InvalidOperationException("first attempt failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public static int CreatedCount;
        public static int DisposedCount;

        public DisposableProbe()
        {
            Interlocked.Increment(ref CreatedCount);
        }

        public static void Reset()
        {
            CreatedCount = 0;
            DisposedCount = 0;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref DisposedCount);
        }
    }

    private sealed class DisposableProbeJob : IDurableJob
    {
        public DisposableProbeJob(DisposableProbe probe)
        {
        }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDisposableProbeJob : IDurableJob
    {
        public FailingDisposableProbeJob(DisposableProbe probe)
        {
        }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
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
