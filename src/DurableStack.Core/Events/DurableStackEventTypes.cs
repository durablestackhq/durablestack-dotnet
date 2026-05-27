namespace DurableStack.Core.Events;

public static class DurableStackEventTypes
{
    public const int CurrentVersion = 2;

    public const string JobClaimed = "job_claimed";
    public const string JobStarted = "job_started";
    public const string JobSucceeded = "job_succeeded";
    public const string JobFailed = "job_failed";
    public const string JobRetried = "job_retried";
    public const string RetryScheduled = "retry_scheduled";
    public const string WorkerHeartbeat = "worker_heartbeat";
}
