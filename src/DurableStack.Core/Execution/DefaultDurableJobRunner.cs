using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Core.Execution;

public sealed class DefaultDurableJobRunner : IDurableJobRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDurableJobRegistry _registry;
    private readonly DurableStackOptions _options;

    public DefaultDurableJobRunner(
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory,
        IDurableJobRegistry registry,
        DurableStackOptions options)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _options = options;
    }

    public async Task RunAsync(JobRunRecord run, CancellationToken cancellationToken)
    {
        var registration = _registry.FindByName(run.JobName)
            ?? throw new InvalidOperationException($"No registration exists for job name '{run.JobName}'.");

        IServiceScope? scope = null;
        var scopedProvider = _serviceProvider;
        if (_options.JobActivation == DurableStackJobActivationMode.ScopedPerExecution)
        {
            scope = _scopeFactory.CreateScope();
            scopedProvider = scope.ServiceProvider;
        }

        try
        {
            var jobContext = new JobContext
            {
                RunId = run.Id,
                JobName = run.JobName,
                Attempt = run.Attempt,
                ScheduledForUtc = run.ScheduledForUtc,
                Services = scopedProvider,
            };

            object instance;
            try
            {
                instance = scopedProvider.GetRequiredService(registration.JobType);
            }
            catch (Exception ex)
            {
                throw CreateActivationException(registration, ex);
            }

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
        finally
        {
            scope?.Dispose();
        }
    }

    private InvalidOperationException CreateActivationException(DurableJobRegistration registration, Exception exception)
    {
        if (_options.JobActivation == DurableStackJobActivationMode.RootProvider)
        {
            return new InvalidOperationException(
                "Job activation failed. DurableStack is running in root activation mode; scoped services are not supported. " +
                "Use scoped-per-execution activation or change job dependencies.",
                exception);
        }

        return new InvalidOperationException(
            $"Job activation failed for '{registration.JobType.FullName}'. Ensure the job and its dependencies are registered in DI.",
            exception);
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
