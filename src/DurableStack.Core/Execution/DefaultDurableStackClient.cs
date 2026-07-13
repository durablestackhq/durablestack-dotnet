using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;

namespace DurableStack.Core.Execution;

/// <summary>
/// Enqueues and cancels job runs against the configured store. The job type is resolved to
/// its registration by CLR type, payloads are serialized to JSON with
/// <c>System.Text.Json</c>, and an <see cref="InvalidOperationException"/> is thrown when
/// the type was never registered.
/// </summary>
public sealed class DefaultDurableStackClient : IDurableStackClient
{
    private readonly IDurableJobStore _store;
    private readonly IDurableJobRegistry _registry;

    /// <summary>
    /// Creates a client that writes runs to <paramref name="store"/> and resolves job
    /// types through <paramref name="registry"/>.
    /// </summary>
    /// <param name="store">Store that persists the enqueued runs.</param>
    /// <param name="registry">Registry used to look up registrations by job type.</param>
    public DefaultDurableStackClient(IDurableJobStore store, IDurableJobRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _store.CancelRunAsync(runId, cancellationToken);
    }

    private static string? SerializePayload(object? payload)
    {
        return payload is null ? null : JsonSerializer.Serialize(payload);
    }
}
