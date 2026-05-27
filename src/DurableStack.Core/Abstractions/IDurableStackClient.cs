using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IDurableStackClient
{
    Task EnqueueAsync<TJob>(object? payload = null, CancellationToken cancellationToken = default);

    Task ScheduleAsync<TJob>(
        object? payload,
        DateTimeOffset runAtUtc,
        CancellationToken cancellationToken = default);
}
