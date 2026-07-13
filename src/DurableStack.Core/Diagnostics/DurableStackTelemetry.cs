using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DurableStack.Core.Diagnostics;

/// <summary>
/// Holds the OpenTelemetry instrumentation surface for DurableStack: the shared
/// <see cref="System.Diagnostics.ActivitySource"/> used for job execution traces and the
/// counters recorded by the processor and lease heartbeat. Subscribe to
/// <see cref="ActivitySourceName"/> and <see cref="MeterName"/> to collect them.
/// </summary>
public static class DurableStackTelemetry
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/> that emits DurableStack traces.</summary>
    public const string ActivitySourceName = "DurableStack";
    /// <summary>Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that emits DurableStack counters.</summary>
    public const string MeterName = "DurableStack";

    /// <summary>Source for job execution activities such as <c>durablestack.job.execute</c>.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    /// <summary>Meter under which all DurableStack counters are created.</summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>Counts processor polling cycles (<c>durablestack.worker.polls</c>).</summary>
    public static readonly Counter<long> WorkerPolls = Meter.CreateCounter<long>("durablestack.worker.polls");
    /// <summary>Counts worker heartbeat signals (<c>durablestack.worker.heartbeats</c>).</summary>
    public static readonly Counter<long> WorkerHeartbeats = Meter.CreateCounter<long>("durablestack.worker.heartbeats");
    /// <summary>Counts runs claimed from the store under a lease (<c>durablestack.jobs.claimed</c>).</summary>
    public static readonly Counter<long> JobsClaimed = Meter.CreateCounter<long>("durablestack.jobs.claimed");
    /// <summary>Counts run executions that began on this worker (<c>durablestack.jobs.started</c>).</summary>
    public static readonly Counter<long> JobsStarted = Meter.CreateCounter<long>("durablestack.jobs.started");
    /// <summary>Counts run executions that completed successfully (<c>durablestack.jobs.succeeded</c>).</summary>
    public static readonly Counter<long> JobsSucceeded = Meter.CreateCounter<long>("durablestack.jobs.succeeded");
    /// <summary>Counts run executions that threw an exception (<c>durablestack.jobs.failed</c>).</summary>
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("durablestack.jobs.failed");
    /// <summary>Counts failed executions that were rescheduled for another attempt (<c>durablestack.jobs.retried</c>).</summary>
    public static readonly Counter<long> JobsRetried = Meter.CreateCounter<long>("durablestack.jobs.retried");
    /// <summary>Counts pending runs created from due recurring schedules (<c>durablestack.recurring.materialized</c>).</summary>
    public static readonly Counter<long> RecurringRunsMaterialized = Meter.CreateCounter<long>("durablestack.recurring.materialized");
    /// <summary>Counts successful lease extensions performed by the heartbeat (<c>durablestack.leases.extended</c>).</summary>
    public static readonly Counter<long> LeaseExtensions = Meter.CreateCounter<long>("durablestack.leases.extended");
}
