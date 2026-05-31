using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IDurableStackClient
{
    Task<Guid> EnqueueAsync<TJob>(object? payload = null, CancellationToken cancellationToken = default);

    Task<Guid> ScheduleAsync<TJob>(
        object? payload,
        DateTimeOffset runAtUtc,
        CancellationToken cancellationToken = default);
}
