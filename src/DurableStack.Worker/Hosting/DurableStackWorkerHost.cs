using System;
using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DurableStack.Worker.Hosting;

public static class DurableStackWorkerHost
{
    public static IHostApplicationBuilder AddDurableStackSqlite(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlite(configuration, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackSqlite(
        this IHostApplicationBuilder builder,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlite(connectionString, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackSqlServer(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlServer(configuration, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackSqlServer(
        this IHostApplicationBuilder builder,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackSqlServer(connectionString, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackMySql(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackMySql(configuration, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackMySql(
        this IHostApplicationBuilder builder,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackMySql(connectionString, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackPostgres(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackPostgres(configuration, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStackPostgres(
        this IHostApplicationBuilder builder,
        string connectionString,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStackPostgres(connectionString, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStack(
        this IHostApplicationBuilder builder,
        IConfiguration configuration,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStack(configuration, configure);
        return builder;
    }

    public static IHostApplicationBuilder AddDurableStack(
        this IHostApplicationBuilder builder,
        Action<DurableStackOptions>? configure = null)
    {
        builder.Services.AddDurableStack(configure);
        return builder;
    }
}
