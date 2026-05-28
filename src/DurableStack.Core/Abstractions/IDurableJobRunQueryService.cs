using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

public interface IDurableJobRunQueryService
{
    Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(string status, int take = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(string jobName, int take = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take = 100, CancellationToken cancellationToken = default);
}
