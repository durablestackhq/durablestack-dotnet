using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

/// <summary>
/// Read-only query surface over persisted job runs, intended for dashboards and diagnostics.
/// Results are point-in-time snapshots; a run's status can change as soon as it is read.
/// </summary>
public interface IDurableJobRunQueryService
{
    /// <summary>
    /// Fetches a single run by id; returns null when no run with that id exists (including
    /// runs already deleted by retention).
    /// </summary>
    Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recently scheduled runs across all jobs and statuses, capped at
    /// <paramref name="take"/> (default 100).
    /// </summary>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns runs currently in the given status ("pending", "leased", "succeeded", or
    /// "failed"), capped at <paramref name="take"/> (default 100).
    /// </summary>
    /// <param name="status">Status value to filter on.</param>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(string status, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns runs belonging to the given registered job name, capped at
    /// <paramref name="take"/> (default 100).
    /// </summary>
    /// <param name="jobName">Registered job name to filter on.</param>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(string jobName, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns runs waiting to execute (enqueued but not yet claimed by a worker), capped at
    /// <paramref name="take"/> (default 100).
    /// </summary>
    /// <param name="take">Maximum number of runs to return.</param>
    /// <param name="cancellationToken">Token that cancels the query.</param>
    Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take = 100, CancellationToken cancellationToken = default);
}
