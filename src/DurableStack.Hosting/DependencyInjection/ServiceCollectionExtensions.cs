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
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.DependencyInjection;

/// <summary>
/// Extension methods for registering the DurableStack worker, job store, and jobs with an
/// <see cref="IServiceCollection"/>. <c>AddDurableStack</c> (or a provider-specific variant such as
/// <c>AddDurableStackPostgres</c>) is the main entry point for consuming applications; individual jobs
/// are then registered with <c>AddDurableJob</c> or discovered automatically from the entry assembly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DurableStack backed by PostgreSQL. Binds the <c>DurableStack</c> configuration
    /// section, takes the connection string from <c>DurableStack:Postgres:ConnectionString</c>,
    /// and otherwise behaves like <see cref="AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no PostgreSQL connection string is configured.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by SQL Server. Binds the <c>DurableStack</c> configuration
    /// section, takes the connection string from <c>DurableStack:SqlServer:ConnectionString</c>,
    /// and otherwise behaves like <see cref="AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no SQL Server connection string is configured.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by MySQL. Binds the <c>DurableStack</c> configuration
    /// section, takes the connection string from <c>DurableStack:MySql:ConnectionString</c>,
    /// and otherwise behaves like <see cref="AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no MySQL connection string is configured.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by PostgreSQL using an explicit connection string. If the service
    /// collection already contains an <see cref="IConfiguration"/> instance, its <c>DurableStack</c>
    /// section is bound first; the <paramref name="connectionString"/> argument takes precedence over
    /// <c>DurableStack:Postgres:ConnectionString</c>. Otherwise behaves like
    /// <see cref="AddDurableStack(IServiceCollection, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="connectionString">PostgreSQL connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no PostgreSQL connection string is available from either source.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by SQLite. Binds the <c>DurableStack</c> configuration
    /// section, takes the connection string from <c>DurableStack:Sqlite:ConnectionString</c>,
    /// and otherwise behaves like <see cref="AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no SQLite connection string is configured.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by SQL Server using an explicit connection string. If the service
    /// collection already contains an <see cref="IConfiguration"/> instance, its <c>DurableStack</c>
    /// section is bound first; the <paramref name="connectionString"/> argument takes precedence over
    /// <c>DurableStack:SqlServer:ConnectionString</c>. Otherwise behaves like
    /// <see cref="AddDurableStack(IServiceCollection, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="connectionString">SQL Server connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no SQL Server connection string is available from either source.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by SQLite using an explicit connection string. If the service
    /// collection already contains an <see cref="IConfiguration"/> instance, its <c>DurableStack</c>
    /// section is bound first; the <paramref name="connectionString"/> argument takes precedence over
    /// <c>DurableStack:Sqlite:ConnectionString</c>. Otherwise behaves like
    /// <see cref="AddDurableStack(IServiceCollection, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="connectionString">SQLite connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no SQLite connection string is available from either source.</exception>
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

    /// <summary>
    /// Registers DurableStack backed by MySQL using an explicit connection string. If the service
    /// collection already contains an <see cref="IConfiguration"/> instance, its <c>DurableStack</c>
    /// section is bound first; the <paramref name="connectionString"/> argument takes precedence over
    /// <c>DurableStack:MySql:ConnectionString</c>. Otherwise behaves like
    /// <see cref="AddDurableStack(IServiceCollection, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="connectionString">MySQL connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no MySQL connection string is available from either source.</exception>
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

    /// <summary>
    /// Registers the complete DurableStack runtime: the job store for the configured storage provider,
    /// the job registry, the enqueue/schedule client (<see cref="IDurableStackClient"/>), the job runner
    /// with lease-heartbeat extension, recurring-job scheduling and sync, the schedule admin and run
    /// query services, event publishing, and the <see cref="DurableStackHostedService"/> background worker.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configuration">Application configuration; the <c>DurableStack</c> section is bound onto <see cref="DurableStackOptions"/>.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// When no storage provider is configured, the non-durable in-memory store is used, which is suitable
    /// only for development and testing. Invalid or missing numeric options are replaced with safe defaults.
    /// When <c>JobRegistration:AutoDiscoverJobsFromAssembly</c> is enabled (the default), the entry assembly
    /// is scanned for <see cref="Core.Abstractions.IDurableJob"/> implementations, honoring
    /// <see cref="DurableJobAttribute"/> and <see cref="RecurringJobAttribute"/>. If <c>Eventing:TenantId</c>
    /// and <c>Eventing:ClientSecret</c> are both set, the hosted observability API ingestion sink and its
    /// sync service are registered automatically.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a database storage provider is selected without a connection string.
    /// </exception>
    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromConfiguration(configuration, configure);
        return services.AddDurableStack(options);
    }

    /// <summary>
    /// Adds DurableStack with explicit poll jitter settings.
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
    [Obsolete("Poll jitter is enabled by default. Use AddDurableStack(...) and set PollJitterRatio if needed.")]
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

    /// <summary>
    /// Registers the complete DurableStack runtime configured in code. If the service collection already
    /// contains an <see cref="IConfiguration"/> instance, its <c>DurableStack</c> section is bound first
    /// and <paramref name="configure"/> is applied on top; otherwise defaults plus <paramref name="configure"/>
    /// are used. With no storage provider configured, the non-durable in-memory store is used.
    /// See <see cref="AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>
    /// for the full set of registered services.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="configure">Optional callback to configure <see cref="DurableStackOptions"/> in code.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a database storage provider is selected without a connection string.
    /// </exception>
    public static IServiceCollection AddDurableStack(
        this IServiceCollection services,
        Action<DurableStackOptions>? configure = null)
    {
        var options = CreateOptionsFromRegisteredConfigurationOrDefault(services, configure);

        return services.AddDurableStack(options);
    }

    /// <summary>
    /// Adds DurableStack with explicit poll jitter settings.
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
    [Obsolete("Poll jitter is enabled by default. Use AddDurableStack(...) and set PollJitterRatio if needed.")]
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
                provider.GetRequiredService<DurableStackOptions>(),
                provider.GetService<ILogger<LeaseHeartbeatJobRunner>>()));
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

    /// <summary>
    /// Discovers and registers durable jobs from the entry assembly (or the calling assembly when no
    /// entry assembly is available). Called automatically by <c>AddDurableStack</c> when
    /// <c>JobRegistration:AutoDiscoverJobsFromAssembly</c> is enabled (the default).
    /// See <see cref="AddDurableJobsFromAssembly(IServiceCollection, Assembly)"/> for discovery rules.
    /// </summary>
    /// <param name="services">The service collection to add the job registrations to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDurableJobsFromAssembly(this IServiceCollection services)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        return services.AddDurableJobsFromAssembly(assembly);
    }

    /// <summary>
    /// Discovers and registers durable jobs from the assembly containing <typeparamref name="TMarker"/>.
    /// See <see cref="AddDurableJobsFromAssembly(IServiceCollection, Assembly)"/> for discovery rules.
    /// </summary>
    /// <typeparam name="TMarker">Any type from the assembly to scan.</typeparam>
    /// <param name="services">The service collection to add the job registrations to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDurableJobsFromAssembly<TMarker>(this IServiceCollection services)
    {
        return services.AddDurableJobsFromAssembly(typeof(TMarker).Assembly);
    }

    /// <summary>
    /// Scans the given assembly for public, concrete <see cref="Core.Abstractions.IDurableJob"/> and
    /// <see cref="Core.Abstractions.IDurableJob{TArgs}"/> implementations and registers each as a durable job.
    /// <see cref="DurableJobAttribute"/> supplies the job name and retry settings (the class name is used
    /// when no name is given); <see cref="RecurringJobAttribute"/> adds a cron schedule whose expression
    /// and IANA time zone are validated during the scan.
    /// </summary>
    /// <param name="services">The service collection to add the job registrations to.</param>
    /// <param name="assembly">The assembly to scan for job types.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a discovered job duplicates an already-registered job name or type, or when a job
    /// declares invalid attribute settings (non-positive max attempts, negative retry delay, missing
    /// cron expression, or an unknown time zone).
    /// </exception>
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

    /// <summary>
    /// Registers a durable job under the given name with settings built through a
    /// <see cref="DurableJobOptions"/> callback (max attempts, cron schedule, retry behavior).
    /// The job type is registered as a transient service so it can take constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="configure">Callback that configures the job's execution and scheduling settings.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Registers an on-demand durable job under the given name. The job type is registered as a
    /// transient service so it can take constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="maxAttempts">Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Registers a recurring durable job that runs on the given cron schedule. Occurrences that would
    /// overlap an active run are skipped. The job type is registered as a transient service so it can
    /// take constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="cronExpression">Cron expression that determines when new runs are materialized.</param>
    /// <param name="timeZone">IANA time zone identifier in which the cron expression is evaluated. Defaults to "UTC".</param>
    /// <param name="maxAttempts">Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Registers a durable job with a strongly typed payload under the given name, with settings built
    /// through a <see cref="DurableJobOptions"/> callback. Enqueued arguments are serialized as JSON and
    /// deserialized to <typeparamref name="TArgs"/> at execution time. The job type is registered as a
    /// transient service so it can take constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <typeparam name="TArgs">The payload type passed to the job on execution.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="configure">Callback that configures the job's execution and scheduling settings.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Registers an on-demand durable job with a strongly typed payload under the given name.
    /// Enqueued arguments are serialized as JSON and deserialized to <typeparamref name="TArgs"/>
    /// at execution time. The job type is registered as a transient service so it can take
    /// constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <typeparam name="TArgs">The payload type passed to the job on execution.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="maxAttempts">Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Registers a recurring durable job with a strongly typed payload that runs on the given cron
    /// schedule. Occurrences that would overlap an active run are skipped. The job type is registered
    /// as a transient service so it can take constructor dependencies.
    /// </summary>
    /// <typeparam name="TJob">The job implementation type.</typeparam>
    /// <typeparam name="TArgs">The payload type passed to the job on execution.</typeparam>
    /// <param name="services">The service collection to add the job registration to.</param>
    /// <param name="name">Unique job name used for enqueuing and stored run records (case-insensitive).</param>
    /// <param name="cronExpression">Cron expression that determines when new runs are materialized.</param>
    /// <param name="timeZone">IANA time zone identifier in which the cron expression is evaluated. Defaults to "UTC".</param>
    /// <param name="maxAttempts">Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the job name or type is already registered.</exception>
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

    /// <summary>
    /// Adds <see cref="LoggingDurableStackEventSink"/> so worker events (job claimed, started,
    /// succeeded, failed, retried, heartbeats) are written to the application's <c>ILogger</c>.
    /// </summary>
    /// <param name="services">The service collection to add the sink to.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection UseDurableStackLoggingEventSink(this IServiceCollection services)
    {
        services.AddSingleton<IDurableStackEventSink, LoggingDurableStackEventSink>();
        return services;
    }

    /// <summary>
    /// Adds the hosted observability API ingestion pipeline: the buffering
    /// <see cref="IngestionDurableStackEventSink"/>, the <see cref="IngestionEventSyncHostedService"/>
    /// that batches and posts events, and its named HTTP client. Safe to call repeatedly; duplicate
    /// registrations are skipped. Called automatically by <c>AddDurableStack</c> when
    /// <c>Eventing:TenantId</c> and <c>Eventing:ClientSecret</c> are both configured — without those
    /// credentials the sync service stays idle.
    /// </summary>
    /// <param name="services">The service collection to add the ingestion pipeline to.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>
    /// Adds a custom event sink that receives all worker events alongside any other registered sinks.
    /// </summary>
    /// <typeparam name="TSink">The <see cref="IDurableStackEventSink"/> implementation to register as a singleton.</typeparam>
    /// <param name="services">The service collection to add the sink to.</param>
    /// <returns>The same service collection for chaining.</returns>
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
