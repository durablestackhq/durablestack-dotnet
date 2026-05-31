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
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);

        var connectionString = ResolveProviderConnectionString(
            configuration,
            options,
            options.Postgres.ConnectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack PostgreSQL registration requires a connection string. " +
                $"Set DurableStack:Postgres:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        options.UsePostgres(connectionString);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);

        var connectionString = ResolveProviderConnectionString(
            configuration,
            options,
            options.SqlServer.ConnectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack SQL Server registration requires a connection string. " +
                $"Set DurableStack:SqlServer:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        options.UseSqlServer(connectionString);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackMySql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);

        var connectionString = ResolveProviderConnectionString(
            configuration,
            options,
            options.MySql.ConnectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack MySQL registration requires a connection string. " +
                $"Set DurableStack:MySql:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        options.UseMySql(connectionString);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackPostgres(
        this IServiceCollection services,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DurableStack PostgreSQL registration requires a non-empty connection string.");
        }

        var options = new DurableStackOptions();
        options.UsePostgres(connectionString);
        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlite(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);

        var connectionString = ResolveProviderConnectionString(
            configuration,
            options,
            options.Sqlite.ConnectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack SQLite registration requires a connection string. " +
                $"Set DurableStack:Sqlite:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        options.UseSqlite(connectionString);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlServer(
        this IServiceCollection services,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DurableStack SQL Server registration requires a non-empty connection string.");
        }

        var options = new DurableStackOptions();
        options.UseSqlServer(connectionString);
        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlite(
        this IServiceCollection services,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DurableStack SQLite registration requires a non-empty connection string.");
        }

        var options = new DurableStackOptions();
        options.UseSqlite(connectionString);
        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackMySql(
        this IServiceCollection services,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DurableStack MySQL registration requires a non-empty connection string.");
        }

        var options = new DurableStackOptions();
        options.UseMySql(connectionString);
        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);

        ApplyConnectionStringFallback(configuration, options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configure?.Invoke(options);

        return services.AddDurableStack(options);
    }

    private static string? ResolveProviderConnectionString(
        IConfiguration configuration,
        DurableStackOptions options,
        string? providerConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(providerConnectionString))
        {
            return providerConnectionString;
        }

        var name = string.IsNullOrWhiteSpace(options.ConnectionStringName)
            ? "DurableStack"
            : options.ConnectionStringName;

        return configuration.GetConnectionString(name);
    }

    private static void ApplyConnectionStringFallback(IConfiguration configuration, DurableStackOptions options)
    {
        if (options.StorageProvider == DurableStackStorageProvider.Postgres)
        {
            var connectionString = ResolveProviderConnectionString(configuration, options, options.Postgres.ConnectionString);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UsePostgres(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.SqlServer)
        {
            var connectionString = ResolveProviderConnectionString(configuration, options, options.SqlServer.ConnectionString);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.Sqlite)
        {
            var connectionString = ResolveProviderConnectionString(configuration, options, options.Sqlite.ConnectionString);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlite(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.MySql)
        {
            var connectionString = ResolveProviderConnectionString(configuration, options, options.MySql.ConnectionString);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseMySql(connectionString);
            }
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

        options.Retention.RunRetentionSeconds = options.Retention
            .GetEffectiveRunRetention(options.StorageProvider)
            .TotalSeconds;
        options.Retention.SweepIntervalSeconds = options.Retention
            .GetEffectiveSweepInterval()
            .TotalSeconds;
        options.Retention.DeleteBatchSize = options.Retention.GetEffectiveDeleteBatchSize();

        if (options.StorageProvider == DurableStackStorageProvider.Postgres && string.IsNullOrWhiteSpace(options.Postgres.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for PostgreSQL, but no connection string was provided. " +
                $"Set DurableStack:Postgres:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.SqlServer && string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQL Server, but no connection string was provided. " +
                $"Set DurableStack:SqlServer:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.Sqlite && string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQLite, but no connection string was provided. " +
                $"Set DurableStack:Sqlite:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.MySql && string.IsNullOrWhiteSpace(options.MySql.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for MySQL, but no connection string was provided. " +
                $"Set DurableStack:MySql:ConnectionString or ConnectionStrings:{options.ConnectionStringName}.");
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
