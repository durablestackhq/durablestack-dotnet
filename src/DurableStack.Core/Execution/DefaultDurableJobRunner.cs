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

/// <summary>
/// Executes a claimed run by resolving the registered job type from dependency injection,
/// deserializing the JSON payload for typed jobs, and invoking <c>ExecuteAsync</c>. When
/// <c>JobActivation</c> is <see cref="DurableStackJobActivationMode.ScopedPerExecution"/>
/// (the default), each execution gets its own service scope, disposed when the run finishes.
/// </summary>
public sealed class DefaultDurableJobRunner : IDurableJobRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDurableJobRegistry _registry;
    private readonly DurableStackOptions _options;

    /// <summary>
    /// Creates a runner that activates jobs from the given container according to the
    /// configured activation mode.
    /// </summary>
    /// <param name="serviceProvider">Root provider used to resolve jobs in root activation mode.</param>
    /// <param name="scopeFactory">Factory used to create a per-execution scope in scoped activation mode.</param>
    /// <param name="registry">Registry used to look up the job registration for each run.</param>
    /// <param name="options">Configuration supplying the job activation mode.</param>
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

    /// <inheritdoc />
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
