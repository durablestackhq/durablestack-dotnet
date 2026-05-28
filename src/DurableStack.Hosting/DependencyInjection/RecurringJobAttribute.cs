using System;

namespace DurableStack.Hosting.DependencyInjection;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RecurringJobAttribute : Attribute
{
    public RecurringJobAttribute(string cron)
    {
        Cron = cron;
    }

    public string Cron { get; }

    public string TimeZone { get; init; } = "UTC";
}
