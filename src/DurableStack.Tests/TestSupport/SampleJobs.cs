using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core;
using DurableStack.Core.Abstractions;

namespace DurableStack.Tests.TestSupport;

internal sealed class TestNoArgsJob : IDurableJob
{
    public static readonly List<JobContext> Executions = new();

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        Executions.Add(context);
        return Task.CompletedTask;
    }
}

internal sealed class TestArgs
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class TestArgsJob : IDurableJob<TestArgs>
{
    public static readonly List<(TestArgs? Args, JobContext Context)> Executions = new();

    public Task ExecuteAsync(TestArgs args, JobContext context, CancellationToken cancellationToken)
    {
        Executions.Add((args, context));
        return Task.CompletedTask;
    }
}

internal sealed class AlwaysFailJob : IDurableJob
{
    public static int ExecutionCount;

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        throw new InvalidOperationException("boom");
    }
}

internal sealed class LongRunningNoArgsJob : IDurableJob
{
    private readonly TimeSpan _delay;

    public LongRunningNoArgsJob(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
    }
}

internal sealed class AtomicCounterJob : IDurableJob
{
    public static int ExecutionCount;

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ExecutionCount);
        return Task.CompletedTask;
    }
}
