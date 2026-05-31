using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Execution;

public sealed class DefaultDurableStackClient : IDurableStackClient
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;

    public DefaultDurableStackClient(IDurableJobStore store, IDurableJobRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public async Task<Guid> EnqueueAsync<TJob>(object? payload = null, CancellationToken cancellationToken = default)
    {
        var registration = _registry.FindByJobType(typeof(TJob))
            ?? throw new InvalidOperationException($"No registration exists for job type '{typeof(TJob).FullName}'.");

        var payloadJson = SerializePayload(payload);

        return await _store.EnqueueAsync(
            registration.JobName,
            registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
            payloadJson,
            DateTimeOffset.UtcNow,
            registration.MaxAttempts,
            cancellationToken);
    }

    public async Task<Guid> ScheduleAsync<TJob>(
        object? payload,
        DateTimeOffset runAtUtc,
        CancellationToken cancellationToken = default)
    {
        var registration = _registry.FindByJobType(typeof(TJob))
            ?? throw new InvalidOperationException($"No registration exists for job type '{typeof(TJob).FullName}'.");

        var payloadJson = SerializePayload(payload);

        return await _store.EnqueueAsync(
            registration.JobName,
            registration.JobType.AssemblyQualifiedName ?? registration.JobType.FullName ?? registration.JobType.Name,
            payloadJson,
            runAtUtc,
            registration.MaxAttempts,
            cancellationToken);
    }

    private static string? SerializePayload(object? payload)
    {
        return payload is null ? null : JsonSerializer.Serialize(payload);
    }
}
