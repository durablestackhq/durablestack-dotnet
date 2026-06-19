using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Diagnostics;
using DurableStack.Core.Events;
using DurableStack.Core.Options;
using DurableStack.Core.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.Hosting;

public sealed class DurableStackHostedService : BackgroundService
{
    private readonly IDurableStackProcessor _processor;
    private readonly IInFlightRunDrainer? _inFlightRunDrainer;
    private readonly IDurableStackStoreMigrator _storeMigrator;
    private readonly IRecurringJobInitializer _recurringInitializer;
    private readonly DurableStackOptions _options;
    private readonly IReadOnlyList<IDurableStackEventSink> _eventSinks;
    private readonly DurableStackEventFactory _eventFactory;
    private readonly ILogger<DurableStackHostedService> _logger;

    public DurableStackHostedService(
        IDurableStackProcessor processor,
        IDurableStackStoreMigrator storeMigrator,
        IRecurringJobInitializer recurringInitializer,
        DurableStackOptions options,
        IEnumerable<IDurableStackEventSink> eventSinks,
        DurableStackEventFactory eventFactory,
        ILogger<DurableStackHostedService> logger)
    {
        _processor = processor;
        _inFlightRunDrainer = processor as IInFlightRunDrainer;
        _storeMigrator = storeMigrator;
        _recurringInitializer = recurringInitializer;
        _options = options;
        _eventSinks = eventSinks as IReadOnlyList<IDurableStackEventSink> ?? new List<IDurableStackEventSink>(eventSinks);
        _eventFactory = eventFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _storeMigrator.MigrateAsync(stoppingToken);
        await _recurringInitializer.InitializeAsync(stoppingToken);

        _logger.LogInformation(
            "DurableStack worker started. WorkerName={WorkerName} ClaimBatchSize={ClaimBatchSize} MaxConcurrentRuns={MaxConcurrentRuns} PollIntervalMs={PollIntervalMs}",
            _options.WorkerName,
            _options.ClaimBatchSize,
            _options.MaxConcurrentRuns,
            _options.PollInterval.TotalMilliseconds);

        var heartbeatTask = RunHeartbeatLoopAsync(stoppingToken);
        var processingTask = RunProcessingLoopAsync(stoppingToken);

        await Task.WhenAll(heartbeatTask, processingTask);
        if (_inFlightRunDrainer is not null)
        {
            await _inFlightRunDrainer.DrainInFlightRunsAsync(stoppingToken);
        }

        _logger.LogInformation("DurableStack worker stopped.");
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var heartbeat = _eventFactory.Create(DurableStackEventTypes.WorkerHeartbeat);
                foreach (var sink in _eventSinks)
                {
                    await sink.PublishAsync(heartbeat, stoppingToken);
                }
                DurableStackTelemetry.WorkerHeartbeats.Add(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack worker heartbeat failure.");
            }

            await Task.Delay(_options.PollInterval, stoppingToken);
        }
    }

    private async Task RunProcessingLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack worker processing loop failure.");
            }

            await Task.Delay(
                ComputePollDelay(_options.PollInterval, _options.PollJitterEnabled, _options.PollJitterRatio),
                stoppingToken);
        }
    }

    internal static TimeSpan ComputePollDelay(
        TimeSpan pollInterval,
        bool pollJitterEnabled,
        double pollJitterRatio,
        double? randomSample = null)
    {
        if (!pollJitterEnabled)
        {
            return pollInterval;
        }

        var ratio = Math.Clamp(pollJitterRatio, 0, 1);
        var sample = randomSample ?? Random.Shared.NextDouble();
        var factor = 1 + ((sample * 2 - 1) * ratio);
        return TimeSpan.FromMilliseconds(Math.Max(1, pollInterval.TotalMilliseconds * factor));
    }
}
