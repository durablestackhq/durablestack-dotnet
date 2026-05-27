using System;
using DurableStack.AspNetCore.Hosting;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Execution;
using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Core.Query;
using DurableStack.Core.Scheduling;
using DurableStack.AspNetCore.Events;
using DurableStack.MySql.DependencyInjection;
using DurableStack.Postgres.DependencyInjection;
using DurableStack.Sqlite.DependencyInjection;
using DurableStack.SqlServer.DependencyInjection;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DurableStack.AspNetCore.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDurableStackPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        var connectionString = options.Postgres.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("DurableStack");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack PostgreSQL registration requires a connection string. " +
                "Set DurableStack:Postgres:ConnectionString or ConnectionStrings:DurableStack.");
        }

        options.UsePostgres(connectionString);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackSqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        var connectionString = options.SqlServer.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("DurableStack");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack SQL Server registration requires a connection string. " +
                "Set DurableStack:SqlServer:ConnectionString or ConnectionStrings:DurableStack.");
        }

        options.UseSqlServer(connectionString);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);
        return services.AddDurableStack(options);
    }

    public static IServiceCollection AddDurableStackMySql(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = new DurableStackOptions();
        configuration.GetSection("DurableStack").Bind(options);

        var connectionString = options.MySql.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("DurableStack");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack MySQL registration requires a connection string. " +
                "Set DurableStack:MySql:ConnectionString or ConnectionStrings:DurableStack.");
        }

        options.UseMySql(connectionString);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);
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

        var connectionString = options.Sqlite.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("DurableStack");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "DurableStack SQLite registration requires a connection string. " +
                "Set DurableStack:Sqlite:ConnectionString or ConnectionStrings:DurableStack.");
        }

        options.UseSqlite(connectionString);

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);
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

        if (options.StorageProvider == DurableStackStorageProvider.Postgres && string.IsNullOrWhiteSpace(options.Postgres.ConnectionString))
        {
            var connectionString = configuration.GetConnectionString("DurableStack");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UsePostgres(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.SqlServer && string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString))
        {
            var connectionString = configuration.GetConnectionString("DurableStack");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.Sqlite && string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString))
        {
            var connectionString = configuration.GetConnectionString("DurableStack");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlite(connectionString);
            }
        }

        if (options.StorageProvider == DurableStackStorageProvider.MySql && string.IsNullOrWhiteSpace(options.MySql.ConnectionString))
        {
            var connectionString = configuration.GetConnectionString("DurableStack");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseMySql(connectionString);
            }
        }

        if (string.IsNullOrWhiteSpace(options.Eventing.Environment))
        {
            options.Eventing.Environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        }

        configure?.Invoke(options);
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

    private static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        DurableStackOptions options)
    {
        if (options.StorageProvider == DurableStackStorageProvider.Postgres && string.IsNullOrWhiteSpace(options.Postgres.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for PostgreSQL, but no connection string was provided. " +
                "Set DurableStack:Postgres:ConnectionString or ConnectionStrings:DurableStack.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.SqlServer && string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQL Server, but no connection string was provided. " +
                "Set DurableStack:SqlServer:ConnectionString or ConnectionStrings:DurableStack.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.Sqlite && string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for SQLite, but no connection string was provided. " +
                "Set DurableStack:Sqlite:ConnectionString or ConnectionStrings:DurableStack.");
        }

        if (options.StorageProvider == DurableStackStorageProvider.MySql && string.IsNullOrWhiteSpace(options.MySql.ConnectionString))
        {
            throw new InvalidOperationException(
                "DurableStack is configured for MySQL, but no connection string was provided. " +
                "Set DurableStack:MySql:ConnectionString or ConnectionStrings:DurableStack.");
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
        services.AddSingleton<IDurableStackClient, DefaultDurableStackClient>();
        services.AddSingleton<DefaultDurableJobRunner>();
        services.AddSingleton<IDurableJobRunner>(provider =>
            new LeaseHeartbeatJobRunner(
                provider.GetRequiredService<DefaultDurableJobRunner>(),
                provider.GetRequiredService<IDurableJobStore>(),
                provider.GetRequiredService<DurableStackOptions>()));
        services.AddSingleton<IRecurringJobScheduler, RecurringJobScheduler>();
        services.AddSingleton<IRecurringJobInitializer, RecurringJobInitializer>();
        services.AddSingleton<DurableStackEventFactory>();
        services.AddSingleton<IDurableStackProcessor, DurableStackProcessor>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDurableStackEventSink, NoOpDurableStackEventSink>());
        AddApiIngestionEventSinkIfConfigured(services, options);
        services.AddSingleton<IDurableJobRunQueryService, DurableJobRunQueryService>();
        services.AddHostedService<DurableStackHostedService>();

        return services;
    }

    public static IServiceCollection AddDurableJob<TJob>(
        this IServiceCollection services,
        string name,
        Action<DurableJobOptions> configure)
        where TJob : class, IDurableJob
    {
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
}
