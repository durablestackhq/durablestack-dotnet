using System;
using DurableStack.Core.Models;

namespace DurableStack.Hosting.DependencyInjection;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DurableJobAttribute : Attribute
{
    public string? Name { get; init; }

    public int MaxAttempts { get; init; } = 3;

    public RetryBehavior RetryBehavior { get; init; } = RetryBehavior.FixedDelay;

    public int RetryInitialDelaySeconds { get; init; }
}
