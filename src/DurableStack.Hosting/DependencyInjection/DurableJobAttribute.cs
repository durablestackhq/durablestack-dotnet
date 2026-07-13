using System;
using DurableStack.Core.Models;

namespace DurableStack.Hosting.DependencyInjection;

/// <summary>
/// Marks an <see cref="Core.Abstractions.IDurableJob"/> implementation for assembly auto-discovery
/// and supplies its registration settings. Applied classes are picked up by
/// <c>AddDurableJobsFromAssembly</c> (invoked automatically when auto-discovery is enabled).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DurableJobAttribute : Attribute
{
    /// <summary>
    /// Overrides the registered job name. When omitted or whitespace, the class name is used.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Maximum number of execution attempts before a run is marked failed permanently. Defaults to 3.
    /// Registration fails if the value is not greater than zero.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Controls how the delay between retry attempts is calculated. Defaults to <see cref="RetryBehavior.FixedDelay"/>.
    /// </summary>
    public RetryBehavior RetryBehavior { get; init; } = RetryBehavior.FixedDelay;

    /// <summary>
    /// Initial delay in seconds before the first retry. Leave at 0 to use the worker-level default;
    /// registration fails if the value is negative.
    /// </summary>
    public int RetryInitialDelaySeconds { get; init; }
}
