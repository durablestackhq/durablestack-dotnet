using System;
using System.Collections.Generic;
using System.Linq;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Scheduling;

namespace DurableStack.Core.Execution;

public sealed class DurableStackJobRegistry : IDurableJobRegistry
{
    private readonly Dictionary<string, DurableJobRegistration> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, DurableJobRegistration> _byType = new();
    private readonly object _gate = new();

    public DurableStackJobRegistry(IEnumerable<DurableJobRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            AddRegistration(registration);
        }
    }

    public void Register<TJob>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob
    {
        var registration = new DurableJobRegistration
        {
            JobName = jobName,
            JobType = typeof(TJob),
            PayloadType = null,
            MaxAttempts = maxAttempts,
        };

        lock (_gate)
        {
            AddRegistration(registration);
        }
    }

    public void Register<TJob, TArgs>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>
    {
        var registration = new DurableJobRegistration
        {
            JobName = jobName,
            JobType = typeof(TJob),
            PayloadType = typeof(TArgs),
            MaxAttempts = maxAttempts,
        };

        lock (_gate)
        {
            AddRegistration(registration);
        }
    }

    public DurableJobRegistration? FindByName(string jobName)
    {
        lock (_gate)
        {
            _byName.TryGetValue(jobName, out var registration);
            return registration;
        }
    }

    public DurableJobRegistration? FindByJobType(Type jobType)
    {
        lock (_gate)
        {
            _byType.TryGetValue(jobType, out var registration);
            return registration;
        }
    }

    public void RegisterRecurring<TJob>(string jobName, string cronExpression, string timeZone = "UTC", int maxAttempts = 3)
        where TJob : class, IDurableJob
    {
        var registration = new DurableJobRegistration
        {
            JobName = jobName,
            JobType = typeof(TJob),
            PayloadType = null,
            MaxAttempts = maxAttempts,
            CronExpression = cronExpression,
            TimeZone = timeZone,
        };

        lock (_gate)
        {
            AddRegistration(registration);
        }
    }

    public void RegisterRecurring<TJob, TArgs>(string jobName, string cronExpression, string timeZone = "UTC", int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>
    {
        var registration = new DurableJobRegistration
        {
            JobName = jobName,
            JobType = typeof(TJob),
            PayloadType = typeof(TArgs),
            MaxAttempts = maxAttempts,
            CronExpression = cronExpression,
            TimeZone = timeZone,
        };

        lock (_gate)
        {
            AddRegistration(registration);
        }
    }

    public IReadOnlyList<DurableJobRegistration> GetRecurringJobs()
    {
        lock (_gate)
        {
            return _byName.Values.Where(x => x.IsRecurring).ToList();
        }
    }

    private void AddRegistration(DurableJobRegistration registration)
    {
        if (registration.IsRecurring)
        {
            _ = TimeZoneResolver.ResolveFromIana(registration.TimeZone);
        }

        if (_byName.ContainsKey(registration.JobName))
        {
            throw new InvalidOperationException($"A job named '{registration.JobName}' is already registered.");
        }

        if (_byType.ContainsKey(registration.JobType))
        {
            throw new InvalidOperationException($"The job type '{registration.JobType.FullName}' is already registered.");
        }

        _byName.Add(registration.JobName, registration);
        _byType.Add(registration.JobType, registration);
    }
}
