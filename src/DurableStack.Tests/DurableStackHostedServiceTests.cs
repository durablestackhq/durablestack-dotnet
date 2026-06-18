using DurableStack.Hosting.Hosting;

namespace DurableStack.Tests;

public sealed class DurableStackHostedServiceTests
{
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
