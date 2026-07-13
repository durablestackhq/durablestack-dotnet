using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Administrative operations on recurring schedules: listing, pausing and resuming,
/// rescheduling, and triggering an immediate run. Changes are written to the store and take
/// effect across all workers.
/// </summary>
public interface IDurableScheduleAdminService
{
    /// <summary>
    /// Returns the recurring schedules stored in the database, including disabled ones by
    /// default.
    /// </summary>
    /// <param name="includeDisabled">When false, only schedules currently enabled are returned.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<RecurringJobState>> ListScheduledJobsAsync(bool includeDisabled = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses or resumes a recurring schedule. Resuming recomputes the next due time from the
    /// stored cron expression so missed occurrences are not executed. Returns false when no
    /// schedule with that name exists.
    /// </summary>
    /// <param name="jobName">Name of the schedule to update.</param>
    /// <param name="enabled">True to resume materialization, false to pause it.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> SetScheduledJobEnabledAsync(string jobName, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a schedule's cron expression and time zone, recomputing its next due time
    /// from now. Returns false when no schedule with that name exists.
    /// </summary>
    /// <param name="jobName">Name of the schedule to update.</param>
    /// <param name="cronExpression">New five-field cron expression.</param>
    /// <param name="timeZone">IANA time zone id the expression is evaluated in. Defaults to "UTC".</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    Task<bool> UpdateScheduledJobCronAsync(string jobName, string cronExpression, string timeZone = "UTC", CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues an immediate run of a recurring job, outside its cron schedule, and returns
    /// the new run's id. Returns null when the name is not a registered recurring job. For
    /// jobs that disallow concurrent runs, throws <see cref="ScheduledJobRunBlockedException"/>
    /// when an active run already exists.
    /// </summary>
    /// <param name="jobName">Name of the recurring job to trigger.</param>
    /// <param name="cancellationToken">Token that cancels the enqueue.</param>
    Task<Guid?> RunScheduledJobNowAsync(string jobName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by <see cref="IDurableScheduleAdminService.RunScheduledJobNowAsync"/> when a
/// manual trigger is refused because the job disallows concurrent runs and already has a
/// pending or executing run.
/// </summary>
public sealed class ScheduledJobRunBlockedException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception for the schedule whose manual trigger was blocked.
    /// </summary>
    /// <param name="jobName">Name of the blocked recurring job.</param>
    public ScheduledJobRunBlockedException(string jobName)
        : base($"Schedule '{jobName}' already has an active run and does not allow concurrent runs.")
    {
        JobName = jobName;
    }

    /// <summary>
    /// The name of the recurring job whose manual trigger was blocked by an active run.
    /// </summary>
    public string JobName { get; }
}
