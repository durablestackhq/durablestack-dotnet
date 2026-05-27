using System;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

public interface IDurableJobRegistry
{
    void Register<TJob>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob;

    void Register<TJob, TArgs>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>;

    void RegisterRecurring<TJob>(string jobName, string cronExpression, string timeZone = "UTC", int maxAttempts = 3)
        where TJob : class, IDurableJob;

    void RegisterRecurring<TJob, TArgs>(string jobName, string cronExpression, string timeZone = "UTC", int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>;

    DurableJobRegistration? FindByName(string jobName);

    DurableJobRegistration? FindByJobType(Type jobType);

    IReadOnlyList<DurableJobRegistration> GetRecurringJobs();
}
