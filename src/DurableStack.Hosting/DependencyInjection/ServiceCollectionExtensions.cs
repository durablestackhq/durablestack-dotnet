using System;
using DurableStack.Hosting.Hosting;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Core.Query;
using DurableStack.Core.Scheduling;
using DurableStack.Hosting.Events;
using DurableStack.MySql.DependencyInjection;
using DurableStack.Postgres.DependencyInjection;
using DurableStack.Sqlite.DependencyInjection;
using DurableStack.SqlServer.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DurableStack.Hosting.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDurableStackPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        var connectionString = ResolveProviderConnectionString(null, options.Postgres.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UsePostgres(connectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.Postgres;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        var connectionString = ResolveProviderConnectionString(null, options.SqlServer.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseSqlServer(connectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.SqlServer;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackMySql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        var connectionString = ResolveProviderConnectionString(null, options.MySql.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseMySql(connectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.MySql;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackPostgres(
        this IServiceCollection services,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        var effectiveConnectionString = ResolveProviderConnectionString(connectionString, options.Postgres.ConnectionString);
        if (!string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            options.UsePostgres(effectiveConnectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.Postgres;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlite(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        var connectionString = ResolveProviderConnectionString(null, options.Sqlite.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.Sqlite;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlServer(
        this IServiceCollection services,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        var effectiveConnectionString = ResolveProviderConnectionString(connectionString, options.SqlServer.ConnectionString);
        if (!string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            options.UseSqlServer(effectiveConnectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.SqlServer;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlite(
        this IServiceCollection services,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        var effectiveConnectionString = ResolveProviderConnectionString(connectionString, options.Sqlite.ConnectionString);
        if (!string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            options.UseSqlite(effectiveConnectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.Sqlite;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackMySql(
        this IServiceCollection services,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        var effectiveConnectionString = ResolveProviderConnectionString(connectionString, options.MySql.ConnectionString);
        if (!string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            options.UseMySql(effectiveConnectionString);
        }
        else
        {
            options.StorageProvider = DurableStackStorageProvider.MySql;
        }

        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        return services.AddDurableStack(options);
    }

    /// <summary>
    /// Adds DurableStack with poll jitter enabled to help spread job claiming load
    /// across workers in distributed environments.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pollJitterRatio">
    /// Poll jitter ratio from <c>0</c> to <c>1</c>.
    /// A value of <c>0.2</c> means each poll delay varies by up to +/-20%.
    /// </param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="configure">Optional additional DurableStack options configuration.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pollJitterRatio"/> is outside the range [0, 1].
    /// </exception>
    public static IServiceCollection AddDurableStackWithJitter(
        this IServiceCollection services,
        double pollJitterRatio,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        ValidatePollJitterRatio(pollJitterRatio);

        return services.AddDurableStack(configuration, options =>
        {
            options.PollJitterEnabled = true;
            options.PollJitterRatio = pollJitterRatio;
            configure?.Invoke(options);
        });
    }

    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        return services.AddDurableStack(options);
    }

    /// <summary>
    /// Adds DurableStack with poll jitter enabled to help spread job claiming load
    /// across workers in distributed environments.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pollJitterRatio">
    /// Poll jitter ratio from <c>0</c> to <c>1</c>.
    /// A value of <c>0.2</c> means each poll delay varies by up to +/-20%.
    /// </param>
    /// <param name="configure">Optional additional DurableStack options configuration.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pollJitterRatio"/> is outside the range [0, 1].
    /// </exception>
    public static IServiceCollection AddDurableStackWithJitter(
        this IServiceCollection services,
        double pollJitterRatio,
        Action<DurableStackOptions>? configure = null)
    {
        ValidatePollJitterRatio(pollJitterRatio);

        return services.AddDurableStack(options =>
        {
            options.PollJitterEnabled = true;
            options.PollJitterRatio = pollJitterRatio;
            configure?.Invoke(options);
        });
    }

    private static DurableStackOptions CreateOptionsFromRegisteredConfigurationOrDefault(
        IServiceCollection services,
        Action<DurableStackOptions>? configure)
    {
        var configuration = TryGetRegisteredConfiguration(services);
        return configuration is null
            ? CreateOptions(configure)
            : CreateOptionsFromConfiguration(configuration, configure);
    }

    private static IConfiguration? TryGetRegisteredConfiguration(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IConfiguration))
            {
                continue;
            }

            if (descriptor.ImplementationInstance is IConfiguration configuration)
            {
                return configuration;
            }
        }

        return null;
    }

    private static DurableStackOptions CreateOptions(Action<DurableStackOptions>? configure)
    {
        var options = new DurableStackOptions();
        configure?.Invoke(options);
        return options;
    }

    private static DurableStackOptions CreateOptionsFromConfiguration(
        IConfiguration configuration,
        Action<DurableStackOptions>? configure)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        configure?.Invoke(options);
        return options;
    }

    private static string? ResolveProviderConnectionString(string? methodConnectionString, string? optionsConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(methodConnectionString))
        {
            return methodConnectionString;
        }

        return optionsConnectionString;
    }

    private static void ValidatePollJitterRatio(double pollJitterRatio)
    {
        if (double.IsNaN(pollJitterRatio) || pollJitterRatio < 0 || pollJitterRatio > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollJitterRatio),
                pollJitterRatio,
                "Poll jitter ratio must be between 0 and 1.");
        }
    }

    private static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        DurableStackOptions options)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            options.PollInterval = TimeSpan.FromSeconds(5);
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            options.LeaseDuration = TimeSpan.FromSeconds(30);
        }

        if (options.ClaimBatchSize <= 0)
        {
            options.ClaimBatchSize = 5;
        }

        if (options.MaxConcurrentRuns <= 0)
        {
            options.MaxConcurrentRuns = 5;
        }

        if (double.IsNaN(options.PollJitterRatio) || options.PollJitterRatio < 0 || options.PollJitterRatio > 1)
        {
            options.PollJitterRatio = 0.2;
        }

        if (string.IsNullOrWhiteSpace(options.WorkerName))
        {
            options.WorkerName = DurableStackOptions.CreateDefaultWorkerName();
        }

        if (options.Eventing.IngestionFlushInterval <= TimeSpan.Zero)
        {
            options.Eventing.IngestionFlushInterval = TimeSpan.FromSeconds(5);
        }

        if (!Enum.IsDefined(options.JobActivation))
        {
            options.JobActivation = DurableStackJobActivationMode.ScopedPerExecution;
        }

        if (!Enum.IsDefined(options.Recurring.RegistrationSync.ExistingJobBehavior))
        {
            options.Recurring.RegistrationSync.ExistingJobBehavior = ExistingRecurringJobBehavior.KeepDatabase;
        }

        if (!Enum.IsDefined(options.Recurring.RegistrationSync.OrphanedJobBehavior))
        {
            options.Recurring.RegistrationSync.OrphanedJobBehavior = OrphanedRecurringJobBehavior.Disable;
        }

        options.Retention.RunRetentionSeconds = options.Retention
            .GetEffectiveRunRetention(options.StorageProvider)
            .TotalSeconds;
        options.Retention.SweepIntervalSeconds = options.Retention
            .GetEffectiveSweepInterval()
            .TotalSeconds;
        options.Retention.DeleteBatchSize = options.Retention.GetEffectiveDeleteBatchSize();
        options.Eventing.MaxErrorDetailLength = options.Eventing.GetEffectiveMaxErrorDetailLength();

        if (options.StorageProvider == DurableStackStorageProvider.Postgres && string.IsNullOrWhiteSpace(options.Postgres.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for PostgreSQL, but no connection string was provided. " +
                "Set DurableStack:Postgres:ConnectionString.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.SqlServer && string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQL Server, but no connection string was provided. " +
                "Set DurableStack:SqlServer:ConnectionString.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.Sqlite && string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQLite, but no connection string was provided. " +
                "Set DurableStack:Sqlite:ConnectionString.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.MySql && string.IsNullOrWhiteSpace(options.MySql.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for MySQL, but no connection string was provided. " +
                "Set DurableStack:MySql:ConnectionString.");
        }

        services.AddSingleton(options);

        if (options.StorageProvider == DurableStackStorageProvider.Postgres)
        {
            services.AddDurableStackPostgresStore(options);
        }
        else if (options.StorageProvider == DurableStackStorageProvider.SqlServer)
        {
            services.AddDurableStackSqlServerStore(options);
        }
        else if (options.StorageProvider == DurableStackStorageProvider.Sqlite)
        {
            services.AddDurableStackSqliteStore(options);
        }
        else if (options.StorageProvider == DurableStackStorageProvider.MySql)
        {
            services.AddDurableStackMySqlStore(options);
        }
        else
        {
            services.AddSingleton<IDurableJobStore, InMemoryJobStore>();
            services.AddSingleton<IDurableStackStoreMigrator, NoOpDurableStackStoreMigrator>();
        }

        services.AddSingleton<IDurableJobRegistry>(provider => new DurableStackJobRegistry(provider.GetServices<DurableJobRegistration>()));
        services.AddSingleton<DurableStackBootstrapState>();
        services.AddSingleton<IDurableStackClient, DefaultDurableStackClient>();
        services.AddSingleton<DefaultDurableJobRunner>();
        services.AddSingleton<IDurableJobRunner>(provider =>
            new LeaseHeartbeatJobRunner(
                provider.GetRequiredService<DefaultDurableJobRunner>(),
                provider.GetRequiredService<IDurableJobStore>(),
                provider.GetRequiredService<DurableStackOptions>()));
        services.AddSingleton<IRecurringJobScheduler, RecurringJobScheduler>();
        services.AddSingleton<IRecurringJobInitializer, RecurringJobInitializer>();
        services.AddSingleton<IDurableScheduleAdminService, DurableScheduleAdminService>();
        services.AddSingleton<DurableStackEventFactory>();
        services.AddSingleton<IDurableStackProcessor, DurableStackProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDurableStackEventSink, NoOpDurableStackEventSink>());
        AddApiIngestionEventSinkIfConfigured(services, options);

        if (options.JobRegistration.AutoDiscoverJobsFromAssembly)
        {
            services.AddDurableJobsFromAssembly();
        }

        services.AddSingleton<IDurableJobRunQueryService, DurableJobRunQueryService>();
        services.AddHostedService<DurableStackHostedService>();

        return services;
    }

    public static IServiceCollection AddDurableJobsFromAssembly(this IServiceCollection services)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        return services.AddDurableJobsFromAssembly(assembly);
    }

    public static IServiceCollection AddDurableJobsFromAssembly<TMarker>(this IServiceCollection services)
    {
        return services.AddDurableJobsFromAssembly(typeof(TMarker).Assembly);
    }

    public static IServiceCollection AddDurableJobsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var discovered = assembly
            .GetTypes()
            .Where(IsDiscoverableDurableJobType)
            .Select(CreateRegistration)
            .ToList();

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTypes = new HashSet<Type>();
        foreach (var registration in GetExistingRegistrations(services))
        {
            seenNames.Add(registration.JobName);
            seenTypes.Add(registration.JobType);
        }

        foreach (var registration in discovered)
        {
            if (!seenNames.Add(registration.JobName))
            {
                throw new InvalidOperationException($"A job named '{registration.JobName}' is already registered.");
            }

            if (!seenTypes.Add(registration.JobType))
            {
                throw new InvalidOperationException($"The job type '{registration.JobType.FullName}' is already registered.");
            }

            services.AddTransient(registration.JobType);
            services.AddSingleton(registration);
        }

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob>(
        this IServiceCollection services,
        string name,
        Action<DurableJobOptions> configure)
        where TJob : class, IDurableJob
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        var options = new DurableJobOptions();
        configure(options);

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            MaxAttempts = options.MaxAttempts,
            CronExpression = options.CronExpression,
            TimeZone = options.TimeZone,
            AllowConcurrentRuns = options.AllowConcurrentRuns,
            RetryBehavior = options.RetryBehavior,
            RetryInitialDelaySeconds = options.RetryInitialDelaySeconds,
        });

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob>(
        this IServiceCollection services,
        string name,
        int maxAttempts = 3)
        where TJob : class, IDurableJob
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            MaxAttempts = maxAttempts,
        });

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob>(
        this IServiceCollection services,
        string name,
        string cronExpression,
        string timeZone = "UTC",
        int maxAttempts = 3)
        where TJob : class, IDurableJob
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            MaxAttempts = maxAttempts,
            CronExpression = cronExpression,
            TimeZone = timeZone,
            AllowConcurrentRuns = false,
        });

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob, TArgs>(
        this IServiceCollection services,
        string name,
        Action<DurableJobOptions> configure)
        where TJob : class, IDurableJob<TArgs>
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        var options = new DurableJobOptions();
        configure(options);

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            PayloadType = typeof(TArgs),
            MaxAttempts = options.MaxAttempts,
            CronExpression = options.CronExpression,
            TimeZone = options.TimeZone,
            AllowConcurrentRuns = options.AllowConcurrentRuns,
            RetryBehavior = options.RetryBehavior,
            RetryInitialDelaySeconds = options.RetryInitialDelaySeconds,
        });

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob, TArgs>(
        this IServiceCollection services,
        string name,
        int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            PayloadType = typeof(TArgs),
            MaxAttempts = maxAttempts,
        });

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob, TArgs>(
        this IServiceCollection services,
        string name,
        string cronExpression,
        string timeZone = "UTC",
        int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>
    {
        EnsureUniqueJobRegistration(services, name, typeof(TJob));

        services.AddTransient<TJob>();
        services.AddSingleton(new DurableJobRegistration
        {
            JobName = name,
            JobType = typeof(TJob),
            PayloadType = typeof(TArgs),
            MaxAttempts = maxAttempts,
            CronExpression = cronExpression,
            TimeZone = timeZone,
            AllowConcurrentRuns = false,
        });

        return services;
    }

    public static IServiceCollection UseDurableStackLoggingEventSink(this IServiceCollection services)
    {
        services.AddSingleton<IDurableStackEventSink, LoggingDurableStackEventSink>();
        return services;
    }

    public static IServiceCollection UseDurableStackApiIngestionEventSink(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IDurableStackEventSink) && d.ImplementationType == typeof(IngestionDurableStackEventSink)))
        {
            services.AddSingleton<IDurableStackEventSink, IngestionDurableStackEventSink>();
        }

        if (!services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(IngestionEventSyncHostedService)))
        {
            services.AddHostedService<IngestionEventSyncHostedService>();
        }

        services.AddHttpClient(nameof(IngestionEventSyncHostedService));
        return services;
    }

    public static IServiceCollection UseDurableStackEventSink<TSink>(this IServiceCollection services)
        where TSink : class, IDurableStackEventSink
    {
        services.AddSingleton<IDurableStackEventSink, TSink>();
        return services;
    }

    private static void AddApiIngestionEventSinkIfConfigured(IServiceCollection services, DurableStackOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Eventing.TenantId)
            || string.IsNullOrWhiteSpace(options.Eventing.ClientSecret))
        {
            return;
        }

        services.UseDurableStackApiIngestionEventSink();
    }

    private static IEnumerable<DurableJobRegistration> GetExistingRegistrations(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(DurableJobRegistration) || descriptor.ImplementationInstance is not DurableJobRegistration registration)
            {
                continue;
            }

            yield return registration;
        }
    }

    private static bool IsDiscoverableDurableJobType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
        {
            return false;
        }

        if (!type.IsPublic && !type.IsNestedPublic)
        {
            return false;
        }

        if (typeof(IDurableJob).IsAssignableFrom(type))
        {
            return true;
        }

        return TryGetDurableJobPayloadType(type, out _);
    }

    private static DurableJobRegistration CreateRegistration(Type jobType)
    {
        var durableAttribute = jobType.GetCustomAttribute<DurableJobAttribute>();
        var recurringAttribute = jobType.GetCustomAttribute<RecurringJobAttribute>();

        var jobName = string.IsNullOrWhiteSpace(durableAttribute?.Name)
            ? jobType.Name
            : durableAttribute!.Name!.Trim();

        var maxAttempts = durableAttribute?.MaxAttempts ?? 3;
        if (maxAttempts <= 0)
        {
            throw new InvalidOperationException(
                $"Job '{jobType.FullName}' has invalid MaxAttempts={maxAttempts}. MaxAttempts must be greater than zero.");
        }

        int? retryInitialDelaySeconds = durableAttribute?.RetryInitialDelaySeconds;
        if (retryInitialDelaySeconds == 0)
        {
            retryInitialDelaySeconds = null;
        }

        if (retryInitialDelaySeconds < 0)
        {
            throw new InvalidOperationException(
                $"Job '{jobType.FullName}' has invalid RetryInitialDelaySeconds={retryInitialDelaySeconds}. RetryInitialDelaySeconds must be greater than zero.");
        }

        string? cronExpression = null;
        var timeZone = "UTC";

        if (recurringAttribute is not null)
        {
            if (string.IsNullOrWhiteSpace(recurringAttribute.Cron))
            {
                throw new InvalidOperationException(
                    $"Job '{jobType.FullName}' has [RecurringJob] but no cron expression was provided.");
            }

            cronExpression = recurringAttribute.Cron.Trim();
            timeZone = string.IsNullOrWhiteSpace(recurringAttribute.TimeZone)
                ? "UTC"
                : recurringAttribute.TimeZone.Trim();

            _ = TimeZoneResolver.ResolveFromIana(timeZone);
        }

        _ = TryGetDurableJobPayloadType(jobType, out var payloadType);

        return new DurableJobRegistration
        {
            JobName = jobName,
            JobType = jobType,
            PayloadType = payloadType,
            MaxAttempts = maxAttempts,
            CronExpression = cronExpression,
            TimeZone = timeZone,
            AllowConcurrentRuns = recurringAttribute?.AllowConcurrentRuns ?? false,
            Enabled = recurringAttribute?.Enabled ?? true,
            RetryBehavior = durableAttribute is null ? null : durableAttribute.RetryBehavior,
            RetryInitialDelaySeconds = retryInitialDelaySeconds,
        };
    }

    private static bool TryGetDurableJobPayloadType(Type jobType, out Type? payloadType)
    {
        payloadType = jobType
            .GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDurableJob<>))
            .Select(x => x.GetGenericArguments()[0])
            .FirstOrDefault();

        return payloadType is not null;
    }

    private static void EnsureUniqueJobRegistration(IServiceCollection services, string jobName, Type jobType)
    {
        foreach (var registration in GetExistingRegistrations(services))
        {
            if (string.Equals(registration.JobName, jobName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A job named '{jobName}' is already registered.");
            }

            if (registration.JobType == jobType)
            {
                throw new InvalidOperationException($"The job type '{jobType.FullName}' is already registered.");
            }
        }
    }
}
