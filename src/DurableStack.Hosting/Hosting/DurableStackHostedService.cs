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

/// <summary>
/// Background service that runs the DurableStack worker inside a .NET generic host. On startup it
/// applies store migrations and syncs recurring job registrations, then runs two loops: a processing
/// loop that claims and executes due runs each poll interval, and a heartbeat loop that publishes
/// worker-heartbeat events. On shutdown it stops claiming new runs, waits up to
/// <see cref="Core.Options.DurableStackOptions.ShutdownDrainTimeout"/> for in-flight runs to finish,
/// then cancels any that remain (their leases expire and another worker reclaims them).
/// Registered automatically by <c>AddDurableStack</c>.
/// </summary>
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

    /// <summary>
    /// Creates the worker service from its collaborators. If <paramref name="processor"/> also
    /// implements <see cref="IInFlightRunDrainer"/>, it is used to drain in-flight runs during shutdown.
    /// </summary>
    /// <param name="processor">Processor invoked each poll cycle to claim and execute due runs.</param>
    /// <param name="storeMigrator">Applies job-store schema migrations before processing starts.</param>
    /// <param name="recurringInitializer">Syncs code-defined recurring registrations into the store before processing starts.</param>
    /// <param name="options">Worker options controlling poll interval, jitter, and shutdown drain timeout.</param>
    /// <param name="eventSinks">Sinks that receive worker heartbeat events.</param>
    /// <param name="eventFactory">Factory used to build the heartbeat events.</param>
    /// <param name="logger">Logger for worker lifecycle and loop diagnostics.</param>
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

    /// <summary>
    /// Runs migrations and recurring-registration sync, then executes the poll and heartbeat loops
    /// until <paramref name="stoppingToken"/> fires, and finally drains in-flight runs. Runs execute
    /// on a separate token that outlives <paramref name="stoppingToken"/> so they get a drain window
    /// instead of being cancelled mid-execution.
    /// </summary>
    /// <param name="stoppingToken">Signals that the host is shutting down.</param>
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

        // Runs execute on a token that stays live after stoppingToken fires, so in-flight
        // jobs get a drain window on shutdown instead of being cancelled mid-execution.
        using var executionCts = new CancellationTokenSource();

        var heartbeatTask = RunHeartbeatLoopAsync(stoppingToken);
        var processingTask = RunProcessingLoopAsync(stoppingToken, executionCts.Token);

        await Task.WhenAll(heartbeatTask, processingTask);
        await DrainInFlightRunsAsync(executionCts);

        _logger.LogInformation("DurableStack worker stopped.");
    }

    private async Task DrainInFlightRunsAsync(CancellationTokenSource executionCts)
    {
        if (_inFlightRunDrainer is null)
        {
            executionCts.Cancel();
            return;
        }

        var drainTimeout = _options.ShutdownDrainTimeout;
        if (drainTimeout > TimeSpan.Zero)
        {
            try
            {
                using var drainCts = new CancellationTokenSource(drainTimeout);
                _logger.LogInformation(
                    "DurableStack worker stopping; waiting up to {DrainTimeoutSeconds:0.#}s for in-flight runs to complete.",
                    drainTimeout.TotalSeconds);
                await _inFlightRunDrainer.DrainInFlightRunsAsync(drainCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "DurableStack shutdown drain timed out after {DrainTimeoutSeconds:0.#}s; cancelling remaining in-flight runs. They will be reclaimed after their leases expire.",
                    drainTimeout.TotalSeconds);
            }
        }

        executionCts.Cancel();

        try
        {
            // Give cancelled runs a moment to observe cancellation and unwind.
            using var abortCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _inFlightRunDrainer.DrainInFlightRunsAsync(abortCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("DurableStack in-flight runs did not stop after cancellation; abandoning them.");
        }
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

            // Delay cancellation must exit the loop cleanly: an unhandled TaskCanceledException
            // here would fault Task.WhenAll in ExecuteAsync and skip the shutdown drain.
            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunProcessingLoopAsync(CancellationToken stoppingToken, CancellationToken executionToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Task<int>? pollTask = null;
            try
            {
                // The poll runs on executionToken so runs it starts survive into the drain
                // window; WaitAsync lets the loop exit promptly on shutdown even if the poll
                // itself is stuck on a slow store call.
                pollTask = _processor.ProcessOnceAsync(executionToken);
                await pollTask.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ObserveAbandonedPoll(pollTask);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack worker processing loop failure.");
            }

            try
            {
                await Task.Delay(
                    ComputePollDelay(_options.PollInterval, _options.PollJitterEnabled, _options.PollJitterRatio),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static void ObserveAbandonedPoll(Task? pollTask)
    {
        _ = pollTask?.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
