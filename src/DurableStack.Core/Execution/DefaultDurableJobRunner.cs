using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;

namespace DurableStack.Core.Execution;

public sealed class DefaultDurableJobRunner : IDurableJobRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDurableJobRegistry _registry;

    public DefaultDurableJobRunner(IServiceProvider serviceProvider, IDurableJobRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
    }

    public async Task RunAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        var registration = _registry.FindByName(run.JobName)
            ?? throw new InvalidOperationException($"No registration exists for job name '{run.JobName}'.");

        var scopedProvider = _serviceProvider;

        var jobContext = new JobContext
        {
            RunId = run.Id,
            JobName = run.JobName,
            Attempt = run.Attempt,
            ScheduledForUtc = run.ScheduledForUtc,
            Services = scopedProvider,
        };

        var instance = scopedProvider.GetService(registration.JobType)
            ?? throw new InvalidOperationException($"Could not resolve job type '{registration.JobType.FullName}' from DI.");

        if (registration.PayloadType is null)
        {
            if (instance is not IDurableJob untypedJob)
            {
                throw new InvalidOperationException($"The registered type '{registration.JobType.FullName}' does not implement IDurableJob.");
            }

            await untypedJob.ExecuteAsync(jobContext, cancellationToken);
            return;
        }

        var payload = DeserializePayload(run.PayloadJson, registration.PayloadType);
        var executeMethod = registration.JobType.GetMethod("ExecuteAsync", new[] { registration.PayloadType, typeof(JobContext), typeof(CancellationToken) })
            ?? throw new InvalidOperationException($"Could not find ExecuteAsync on '{registration.JobType.FullName}' for payload type '{registration.PayloadType.FullName}'.");

        var task = executeMethod.Invoke(instance, new[] { payload, jobContext, cancellationToken }) as Task
            ?? throw new InvalidOperationException($"ExecuteAsync invocation on '{registration.JobType.FullName}' did not return Task.");

        await task;
    }

    private static object? DeserializePayload(string? payloadJson, Type payloadType)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return payloadType.IsValueType
                ? Activator.CreateInstance(payloadType)
                : null;
        }

        return JsonSerializer.Deserialize(payloadJson, payloadType);
    }
}
