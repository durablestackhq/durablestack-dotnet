using System;
using System.Linq;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Hosting.Events;
using DurableStack.Hosting.Hosting;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DurableStack.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDurableJob_with_configure_registers_recurring_no_args_job()
    {
        var services = new ServiceCollection();

        services.AddDurableJob<NoArgsTestJob>("heartbeat", job =>
        {
            job.RunOnCron("* * * * *", "UTC");
            job.WithMaxAttempts(5);
        });

        var registration = Assert.Single(services, d => d.ServiceType == typeof(DurableJobRegistration)).ImplementationInstance as DurableJobRegistration;

        Assert.NotNull(registration);
        Assert.Equal("heartbeat", registration!.JobName);
        Assert.Equal(typeof(NoArgsTestJob), registration.JobType);
        Assert.Equal(5, registration.MaxAttempts);
        Assert.Equal("* * * * *", registration.CronExpression);
        Assert.Equal("UTC", registration.TimeZone);
    }

    [Fact]
    public void AddDurableJob_with_configure_registers_args_job()
    {
        var services = new ServiceCollection();

        services.AddDurableJob<ArgsTestJob, DemoArgs>("arg-job", job =>
        {
            job.WithMaxAttempts(2);
        });

        var registration = Assert.Single(services, d => d.ServiceType == typeof(DurableJobRegistration)).ImplementationInstance as DurableJobRegistration;

        Assert.NotNull(registration);
        Assert.Equal("arg-job", registration!.JobName);
        Assert.Equal(typeof(ArgsTestJob), registration.JobType);
        Assert.Equal(typeof(DemoArgs), registration.PayloadType);
        Assert.Equal(2, registration.MaxAttempts);
        Assert.Null(registration.CronExpression);
    }

    [Fact]
    public void DurableJobOptions_WithMaxAttempts_throws_for_non_positive_values()
    {
        var options = new DurableJobOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithMaxAttempts(0));
    }

    [Fact]
    public void DurableJobOptions_RunOnCron_throws_for_invalid_input()
    {
        var options = new DurableJobOptions();

        Assert.Throws<ArgumentException>(() => options.RunOnCron("", "UTC"));
        Assert.Throws<ArgumentException>(() => options.RunOnCron("* * * * *", ""));
    }

    [Fact]
    public void UseDurableStackLoggingEventSink_registers_logging_sink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableStack();

        services.UseDurableStackLoggingEventSink();

        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IDurableStackEventSink>();
        Assert.IsType<LoggingDurableStackEventSink>(sink);
    }

    [Fact]
    public void UseDurableStackEventSink_registers_custom_sink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableStack();

        services.UseDurableStackEventSink<TestEventSink>();

        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IDurableStackEventSink>();
        Assert.IsType<TestEventSink>(sink);
    }

    [Fact]
    public void AddDurableStack_with_eventing_credentials_registers_api_ingestion_sink()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDurableStack(options =>
        {
            options.Eventing.TenantId = "tenant-alpha";
            options.Eventing.ClientSecret = "secret-alpha";
        });

        using var provider = services.BuildServiceProvider();
        var sinks = provider.GetServices<IDurableStackEventSink>().ToList();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(sinks, sink => sink is IngestionDurableStackEventSink);
        Assert.Contains(hostedServices, hosted => hosted is IngestionEventSyncHostedService);
    }

    [Fact]
    public void AddDurableStack_with_credentials_and_custom_sink_registers_both()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDurableStack(options =>
        {
            options.Eventing.TenantId = "tenant-alpha";
            options.Eventing.ClientSecret = "secret-alpha";
        });
        services.UseDurableStackEventSink<TestEventSink>();

        using var provider = services.BuildServiceProvider();
        var sinks = provider.GetServices<IDurableStackEventSink>().ToList();

        Assert.Contains(sinks, sink => sink is IngestionDurableStackEventSink);
        Assert.Contains(sinks, sink => sink is TestEventSink);
    }

    [Fact]
    public void AddDurableJobsFromAssemblyContaining_registers_jobs_with_defaults_and_attributes()
    {
        var services = new ServiceCollection();

        services.AddDurableJobsFromAssembly<DiscoveryDefaultJob>();

        var registrations = services
            .Where(d => d.ServiceType == typeof(DurableJobRegistration))
            .Select(d => Assert.IsType<DurableJobRegistration>(d.ImplementationInstance))
            .ToList();

        Assert.Contains(registrations, x =>
            x.JobType == typeof(DiscoveryDefaultJob)
            && x.JobName == nameof(DiscoveryDefaultJob)
            && x.MaxAttempts == 3
            && x.CronExpression is null);

        Assert.Contains(registrations, x =>
            x.JobType == typeof(DiscoveryRecurringJob)
            && x.JobName == "discovery-recurring-job"
            && x.MaxAttempts == 5
            && x.CronExpression == "*/5 * * * *"
            && x.TimeZone == "UTC");

        Assert.Contains(registrations, x =>
            x.JobType == typeof(DiscoveryArgsJob)
            && x.PayloadType == typeof(DiscoveryPayload)
            && x.MaxAttempts == 4);
    }

    [Fact]
    public void AddDurableJobsFromAssemblyContaining_throws_when_duplicate_name_exists()
    {
        var services = new ServiceCollection();
        services.AddDurableJob<NoArgsTestJob>(nameof(DiscoveryDefaultJob));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDurableJobsFromAssembly<DiscoveryDefaultJob>());

        Assert.Contains("already registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddDurableStack_can_disable_auto_discovery()
    {
        var services = new ServiceCollection();

        services.AddDurableStack(options =>
        {
            options.JobRegistration.AutoDiscoverJobsFromAssembly = false;
        });

        var registrationDescriptors = services.Where(d => d.ServiceType == typeof(DurableJobRegistration)).ToList();
        Assert.Empty(registrationDescriptors);
    }

    [Fact]
    public void AddDurableStack_uses_seconds_aliases_for_poll_and_lease()
    {
        var services = new ServiceCollection();

        services.AddDurableStack(options =>
        {
            options.PollIntervalSeconds = 0.5;
            options.LeaseDurationSeconds = 5;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(TimeSpan.FromMilliseconds(500), options.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.LeaseDuration);
    }

    [Fact]
    public void AddDurableStack_reverts_invalid_poll_and_lease_to_defaults()
    {
        var services = new ServiceCollection();

        services.AddDurableStack(options =>
        {
            options.PollInterval = TimeSpan.Zero;
            options.LeaseDuration = TimeSpan.FromSeconds(-1);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LeaseDuration);
    }

    [Fact]
    public void AddDurableStack_defaults_job_activation_to_scoped_per_execution()
    {
        var services = new ServiceCollection();
        services.AddDurableStack();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackJobActivationMode.ScopedPerExecution, options.JobActivation);
    }

    [Fact]
    public void AddDurableStack_uses_provider_aware_retention_defaults()
    {
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddDurableStack();

        using var inMemoryProvider = inMemoryServices.BuildServiceProvider();
        var inMemoryOptions = inMemoryProvider.GetRequiredService<DurableStackOptions>();
        Assert.Equal(3600d, inMemoryOptions.Retention.RunRetentionSeconds);

        var postgresServices = new ServiceCollection();
        postgresServices.AddDurableStack(options =>
        {
            options.StorageProvider = DurableStackStorageProvider.Postgres;
            options.Postgres.ConnectionString = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres";
        });

        using var postgresProvider = postgresServices.BuildServiceProvider();
        var postgresOptions = postgresProvider.GetRequiredService<DurableStackOptions>();
        Assert.Equal(86400d, postgresOptions.Retention.RunRetentionSeconds);
    }

    [Fact]
    public void AddDurableStack_reverts_invalid_retention_settings_to_defaults()
    {
        var services = new ServiceCollection();
        services.AddDurableStack(options =>
        {
            options.Retention.RunRetentionSeconds = 0;
            options.Retention.SweepIntervalSeconds = -10;
            options.Retention.DeleteBatchSize = 0;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(3600d, options.Retention.RunRetentionSeconds);
        Assert.Equal(300d, options.Retention.SweepIntervalSeconds);
        Assert.Equal(1000, options.Retention.DeleteBatchSize);
    }

    [Fact]
    public void AddDurableJob_throws_when_job_was_already_discovered()
    {
        var services = new ServiceCollection();
        services.AddDurableJobsFromAssembly<DiscoveryDefaultJob>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDurableJob<DiscoveryDefaultJob>(nameof(DiscoveryDefaultJob)));

        Assert.Contains("already registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeDurableStackAsync_runs_bootstrap_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableStack(options =>
        {
            options.JobRegistration.AutoDiscoverJobsFromAssembly = false;
        });

        services.AddSingleton<CountingMigrator>();
        services.AddSingleton<IDurableStackStoreMigrator>(provider => provider.GetRequiredService<CountingMigrator>());
        services.AddSingleton<CountingRecurringInitializer>();
        services.AddSingleton<IRecurringJobInitializer>(provider => provider.GetRequiredService<CountingRecurringInitializer>());

        using var provider = services.BuildServiceProvider();

        await provider.InitializeDurableStackAsync();
        await provider.InitializeDurableStackAsync();

        var migrator = provider.GetRequiredService<CountingMigrator>();
        var recurring = provider.GetRequiredService<CountingRecurringInitializer>();

        Assert.Equal(1, migrator.CallCount);
        Assert.Equal(1, recurring.CallCount);
    }

    private sealed class NoArgsTestJob : IDurableJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DemoArgs
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ArgsTestJob : IDurableJob<DemoArgs>
    {
        public Task ExecuteAsync(DemoArgs args, JobContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestEventSink : IDurableStackEventSink
    {
        public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CountingMigrator : IDurableStackStoreMigrator
    {
        public int CallCount { get; private set; }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingRecurringInitializer : IRecurringJobInitializer
    {
        public int CallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
