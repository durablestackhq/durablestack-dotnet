using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;

namespace DurableStack.Core.Query;

public sealed class DurableJobRunQueryService : IDurableJobRunQueryService
{
    private readonly IDurableJobStore _store;

    public DurableJobRunQueryService(IDurableJobStore store)
    {
        _store = store;
    }

    public Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return _store.GetRunAsync(runId, cancellationToken);
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        var runs = await _store.GetRunsAsync(cancellationToken);
        return runs
            .OrderByDescending(x => x.ScheduledForUtc)
            .Take(Math.Max(1, take))
            .ToList();
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(
        string status,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var runs = await _store.GetRunsAsync(cancellationToken);
        return runs
            .Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ScheduledForUtc)
            .Take(Math.Max(1, take))
            .ToList();
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(
        string jobName,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _store.GetRunsByJobNameAsync(jobName, Math.Max(1, take), cancellationToken);
    }

    public async Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        return await _store.GetEnqueuedRunsAsync(Math.Max(1, take), cancellationToken);
    }
}
