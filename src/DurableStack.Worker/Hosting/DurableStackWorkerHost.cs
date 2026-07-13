using System;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DurableStack.Worker.Hosting;

/// <summary>
/// <see cref="IHostApplicationBuilder"/> extension methods for building standalone DurableStack worker
/// processes. Each method forwards to the corresponding <see cref="ServiceCollectionExtensions"/>
/// registration on <c>builder.Services</c>, keeping worker <c>Program.cs</c> files to a single call.
/// </summary>
public static class DurableStackWorkerHost
{
    /// <summary>
    /// Registers the DurableStack worker backed by SQLite, binding the <c>DurableStack</c>
    /// configuration section. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackSqlite(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackSqlite(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlite(configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by SQLite using an explicit connection string.
    /// Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackSqlite(IServiceCollection, string, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="connectionString">SQLite connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackSqlite(
        this IHostApplicationBuilder builder,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlite(connectionString, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by SQL Server, binding the <c>DurableStack</c>
    /// configuration section. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackSqlServer(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackSqlServer(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlServer(configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by SQL Server using an explicit connection string.
    /// Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackSqlServer(IServiceCollection, string, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="connectionString">SQL Server connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackSqlServer(
        this IHostApplicationBuilder builder,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlServer(connectionString, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by MySQL, binding the <c>DurableStack</c>
    /// configuration section. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackMySql(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackMySql(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackMySql(configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by MySQL using an explicit connection string.
    /// Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackMySql(IServiceCollection, string, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="connectionString">MySQL connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackMySql(
        this IHostApplicationBuilder builder,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackMySql(connectionString, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by PostgreSQL, binding the <c>DurableStack</c>
    /// configuration section. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackPostgres(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackPostgres(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackPostgres(configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers the DurableStack worker backed by PostgreSQL using an explicit connection string.
    /// Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStackPostgres(IServiceCollection, string, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="connectionString">PostgreSQL connection string; when <see langword="null"/>, the value bound from configuration is used.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStackPostgres(
        this IHostApplicationBuilder builder,
        string? connectionString = null,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackPostgres(connectionString, configure);
        return builder;
    }

    /// <summary>
    /// Registers the complete DurableStack worker runtime, binding the <c>DurableStack</c>
    /// configuration section to select the storage provider. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStack(IServiceCollection, IConfiguration, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configuration">Application configuration containing the <c>DurableStack</c> section.</param>
    /// <param name="configure">Optional callback applied after configuration binding to adjust options in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStack(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStack(configuration, configure);
        return builder;
    }

    /// <summary>
    /// Registers the complete DurableStack worker runtime configured in code; with no storage provider
    /// configured, the non-durable in-memory store is used. Forwards to
    /// <see cref="ServiceCollectionExtensions.AddDurableStack(IServiceCollection, Action{DurableStackOptions})"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose services receive the registrations.</param>
    /// <param name="configure">Optional callback to configure <see cref="DurableStackOptions"/> in code.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddDurableStack(
        this IHostApplicationBuilder builder,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStack(configure);
        return builder;
    }
}
