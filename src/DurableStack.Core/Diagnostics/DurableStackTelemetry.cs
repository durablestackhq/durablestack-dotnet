using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DurableStack.Core.Diagnostics;

public static class DurableStackTelemetry
{
    public const string ActivitySourceName = "DurableStack";
    public const string MeterName = "DurableStack";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> WorkerPolls = Meter.CreateCounter<long>("durablestack.worker.polls");
    public static readonly Counter<long> WorkerHeartbeats = Meter.CreateCounter<long>("durablestack.worker.heartbeats");
    public static readonly Counter<long> JobsClaimed = Meter.CreateCounter<long>("durablestack.jobs.claimed");
    public static readonly Counter<long> JobsStarted = Meter.CreateCounter<long>("durablestack.jobs.started");
    public static readonly Counter<long> JobsSucceeded = Meter.CreateCounter<long>("durablestack.jobs.succeeded");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("durablestack.jobs.failed");
    public static readonly Counter<long> JobsRetried = Meter.CreateCounter<long>("durablestack.jobs.retried");
    public static readonly Counter<long> RecurringRunsMaterialized = Meter.CreateCounter<long>("durablestack.recurring.materialized");
    public static readonly Counter<long> LeaseExtensions = Meter.CreateCounter<long>("durablestack.leases.extended");
}
