using System;
using System.Linq;
using System.Threading;
using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Tests.TestSupport;

namespace DurableStack.Tests;

public sealed class DurableStackClientTests
{
    [Fact]
    public async Task EnqueueAsync_creates_pending_run_for_registered_job()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "send-email",
                JobType = typeof(TestArgsJob),
                PayloadType = typeof(TestArgs),
                MaxAttempts = 5,
            },
        });

        var client = new DefaultDurableStackClient(store, registry);

        await client.EnqueueAsync<TestArgsJob>(new TestArgs { Value = "hello" }, CancellationToken.None);

        var runs = await store.GetRunsAsync(CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("send-email", run.JobName);
        Assert.Equal("pending", run.Status);
        Assert.Equal(5, run.MaxAttempts);
        Assert.Contains("hello", run.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleAsync_respects_scheduled_time()
    {
        var store = new InMemoryJobStore();
        var registry = new DurableStackJobRegistry(new[]
        {
            new DurableJobRegistration
            {
                JobName = "future-job",
                JobType = typeof(TestNoArgsJob),
                MaxAttempts = 2,
            },
        });

        var client = new DefaultDurableStackClient(store, registry);
        var scheduledFor = DateTimeOffset.UtcNow.AddMinutes(10);

        await client.ScheduleAsync<TestNoArgsJob>(payload: null, runAtUtc: scheduledFor, cancellationToken: CancellationToken.None);

        var run = (await store.GetRunsAsync(CancellationToken.None)).Single();
        Assert.Equal(scheduledFor, run.ScheduledForUtc);
    }
}
