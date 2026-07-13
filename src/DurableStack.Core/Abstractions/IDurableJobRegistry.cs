using System;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Holds the in-process catalog of job registrations: the mapping from a stable job name
/// to the CLR type that executes it, plus retry and recurrence settings. Workers consult
/// the registry to activate the correct job type for a claimed run; recurring entries are
/// additionally synced to schedule rows in the store at startup.
/// </summary>
public interface IDurableJobRegistry
{
    /// <summary>
    /// Registers a fire-and-forget job with no typed payload under a stable name.
    /// </summary>
    /// <typeparam name="TJob">The job implementation resolved from dependency injection.</typeparam>
    /// <param name="jobName">Stable, unique name stored with each run; renaming it orphans existing runs.</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before a run is terminally failed. Defaults to 3.</param>
    void Register<TJob>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob;

    /// <summary>
    /// Registers a fire-and-forget job that receives a <typeparamref name="TArgs"/> payload
    /// deserialized from the run's stored JSON.
    /// </summary>
    /// <typeparam name="TJob">The job implementation resolved from dependency injection.</typeparam>
    /// <typeparam name="TArgs">The payload type deserialized for each execution.</typeparam>
    /// <param name="jobName">Stable, unique name stored with each run; renaming it orphans existing runs.</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before a run is terminally failed. Defaults to 3.</param>
    void Register<TJob, TArgs>(string jobName, int maxAttempts = 3)
        where TJob : class, IDurableJob<TArgs>;

    /// <summary>
    /// Registers a recurring (cron-scheduled) job with no typed payload. The schedule is
    /// persisted to the store at startup and materialized into runs exactly once per cron slot.
    /// </summary>
    /// <typeparam name="TJob">The job implementation resolved from dependency injection.</typeparam>
    /// <param name="jobName">Stable, unique name identifying both the job and its schedule row.</param>
    /// <param name="cronExpression">Standard five-field cron expression that determines when runs are created.</param>
    /// <param name="timeZone">IANA time zone id the cron expression is evaluated in. Defaults to "UTC".</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before a run is terminally failed. Defaults to 3.</param>
    /// <param name="allowConcurrentRuns">When false (the default), a new occurrence is skipped while a previous run of this job is still pending or executing.</param>
    void RegisterRecurring<TJob>(
        string jobName,
        string cronExpression,
        string timeZone = "UTC",
        int maxAttempts = 3,
        bool allowConcurrentRuns = false)
        where TJob : class, IDurableJob;

    /// <summary>
    /// Registers a recurring (cron-scheduled) job that receives a <typeparamref name="TArgs"/>
    /// payload. The schedule is persisted to the store at startup and materialized into runs
    /// exactly once per cron slot. Materialized runs carry a null payload.
    /// </summary>
    /// <typeparam name="TJob">The job implementation resolved from dependency injection.</typeparam>
    /// <typeparam name="TArgs">The payload type deserialized for each execution.</typeparam>
    /// <param name="jobName">Stable, unique name identifying both the job and its schedule row.</param>
    /// <param name="cronExpression">Standard five-field cron expression that determines when runs are created.</param>
    /// <param name="timeZone">IANA time zone id the cron expression is evaluated in. Defaults to "UTC".</param>
    /// <param name="maxAttempts">Total attempts (initial execution plus retries) before a run is terminally failed. Defaults to 3.</param>
    /// <param name="allowConcurrentRuns">When false (the default), a new occurrence is skipped while a previous run of this job is still pending or executing.</param>
    void RegisterRecurring<TJob, TArgs>(
        string jobName,
        string cronExpression,
        string timeZone = "UTC",
        int maxAttempts = 3,
        bool allowConcurrentRuns = false)
        where TJob : class, IDurableJob<TArgs>;

    /// <summary>
    /// Looks up a registration by job name; returns null when no job is registered under that name.
    /// </summary>
    DurableJobRegistration? FindByName(string jobName);

    /// <summary>
    /// Looks up a registration by the CLR type that implements the job; returns null when the
    /// type has not been registered.
    /// </summary>
    DurableJobRegistration? FindByJobType(Type jobType);

    /// <summary>
    /// Returns the registrations that carry a cron expression, i.e. the schedules synced to the
    /// store at startup.
    /// </summary>
    IReadOnlyList<DurableJobRegistration> GetRecurringJobs();

    /// <summary>
    /// Returns every registration in the catalog, recurring and fire-and-forget alike.
    /// </summary>
    IReadOnlyList<DurableJobRegistration> GetAllJobs();
}
