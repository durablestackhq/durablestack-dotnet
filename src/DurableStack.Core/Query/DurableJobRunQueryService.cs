using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;

namespace DurableStack.Core.Query;

/// <summary>
/// Read-only facade over the configured <see cref="IDurableJobStore"/> for inspecting job
/// runs, intended for dashboards and diagnostics. Requested result counts are clamped to a
/// minimum of one before hitting the store.
/// </summary>
public sealed class DurableJobRunQueryService : IDurableJobRunQueryService
{
    private readonly IDurableJobStore _store;

    /// <summary>
    /// Creates a query service that reads from <paramref name="store"/>.
    /// </summary>
    /// <param name="store">Store whose run records are queried.</param>
    public DurableJobRunQueryService(IDurableJobStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return _store.GetRunAsync(runId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        return await _store.GetRecentRunsAsync(Math.Max(1, take), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _store.GetRunsByStatusAsync(status, Math.Max(1, take), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _store.GetRunsByJobNameAsync(jobName, Math.Max(1, take), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        return await _store.GetEnqueuedRunsAsync(Math.Max(1, take), cancellationToken);
    }
}
