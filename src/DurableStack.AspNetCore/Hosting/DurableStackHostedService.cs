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

namespace DurableStack.AspNetCore.Hosting;

public sealed class DurableStackHostedService : BackgroundService
{
    private readonly IDurableStackProcessor _processor;
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
            "DurableStack worker started. WorkerName={WorkerName} BatchSize={BatchSize} PollIntervalMs={PollIntervalMs}",
            _options.WorkerName,
            _options.BatchSize,
            _options.PollInterval.TotalMilliseconds);

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

                await _processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack worker loop failure.");
            }

            await Task.Delay(_options.PollInterval, stoppingToken);
        }

        _logger.LogInformation("DurableStack worker stopped.");
    }
}
