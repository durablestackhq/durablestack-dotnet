using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using DurableStack.Core.Execution;
using DurableStack.Core.Options;
using DurableStack.Hosting.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DurableStack.Tests;

public sealed class DurableStackHostedServiceTests
{
    [Fact]
    public async Task StopAsync_drains_in_flight_runs_before_cancelling_execution()
    {
        var processor = new FakeDrainableProcessor();
        var options = new DurableStackOptions
        {
            WorkerName = "test-worker",
            PollInterval = TimeSpan.FromMilliseconds(50),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        };

        var service = new DurableStackHostedService(
            processor,
            new NoOpDurableStackStoreMigrator(),
            new NoOpRecurringJobInitializer(),
            options,
            Array.Empty<IDurableStackEventSink>(),
            new DurableStackEventFactory(options),
            NullLogger<DurableStackHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await processor.FirstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        Assert.True(processor.DrainCalled);
        // The drain window must run before execution is cancelled, and execution must be
        // cancelled by the time shutdown completes.
        Assert.False(processor.ExecutionTokenCancelledAtFirstDrain);
        Assert.True(processor.CapturedExecutionToken.IsCancellationRequested);
    }

    private sealed class FakeDrainableProcessor : IDurableStackProcessor, IInFlightRunDrainer
    {
        private int _drainCalls;

        public TaskCompletionSource FirstPoll { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CapturedExecutionToken { get; private set; }

        public bool DrainCalled => Volatile.Read(ref _drainCalls) > 0;

        public bool ExecutionTokenCancelledAtFirstDrain { get; private set; }

        public Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
        {
            CapturedExecutionToken = cancellationToken;
            FirstPoll.TrySetResult();
            return Task.FromResult(0);
        }

        public Task DrainInFlightRunsAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _drainCalls) == 1)
            {
                ExecutionTokenCancelledAtFirstDrain = CapturedExecutionToken.IsCancellationRequested;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoOpRecurringJobInitializer : IRecurringJobInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ComputePollDelay_returns_poll_interval_when_jitter_disabled()
    {
        var pollInterval = TimeSpan.FromSeconds(5);

        var delay = DurableStackHostedService.ComputePollDelay(
            pollInterval,
            pollJitterEnabled: false,
            pollJitterRatio: 0.2,
            randomSample: 0.9);

        Assert.Equal(pollInterval, delay);
    }

    [Fact]
    public void ComputePollDelay_with_jitter_applies_symmetric_bounds()
    {
        var pollInterval = TimeSpan.FromSeconds(5);

        var minDelay = DurableStackHostedService.ComputePollDelay(
            pollInterval,
            pollJitterEnabled: true,
            pollJitterRatio: 0.2,
            randomSample: 0);

        var maxDelay = DurableStackHostedService.ComputePollDelay(
            pollInterval,
            pollJitterEnabled: true,
            pollJitterRatio: 0.2,
            randomSample: 1);

        Assert.Equal(TimeSpan.FromSeconds(4), minDelay);
        Assert.Equal(TimeSpan.FromSeconds(6), maxDelay);
    }

    [Fact]
    public void ComputePollDelay_clamps_out_of_range_jitter_ratio()
    {
        var pollInterval = TimeSpan.FromSeconds(5);

        var lowRatioDelay = DurableStackHostedService.ComputePollDelay(
            pollInterval,
            pollJitterEnabled: true,
            pollJitterRatio: -1,
            randomSample: 0);

        var highRatioDelay = DurableStackHostedService.ComputePollDelay(
            pollInterval,
            pollJitterEnabled: true,
            pollJitterRatio: 5,
            randomSample: 0);

        Assert.Equal(pollInterval, lowRatioDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(1), highRatioDelay);
    }
}
