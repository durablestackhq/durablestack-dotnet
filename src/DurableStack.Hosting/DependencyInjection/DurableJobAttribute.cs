using System;

namespace DurableStack.Hosting.DependencyInjection;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DurableJobAttribute : Attribute
{
    public string? Name { get; init; }

    public int MaxAttempts { get; init; } = 3;
}
