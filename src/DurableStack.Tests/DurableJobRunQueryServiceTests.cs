using DurableStack.Core.Execution;
using DurableStack.Core.Models;
using DurableStack.Core.Query;

namespace DurableStack.Tests;

public sealed class DurableJobRunQueryServiceTests
{
    [Fact]
    public async Task Supports_job_name_and_enqueued_only_filters()
    {
        var store = new InMemoryJobStore();
        var query = new DurableJobRunQueryService(store);

        _ = await store.EnqueueAsync(
            "send-email",
            "job-type-a",
            payloadJson: null,
            DateTimeOffset.UtcNow,
            maxAttempts: 3,
            CancellationToken.None);

        var recurringRegistration = new DurableJobRegistration
        {
            JobName = "heartbeat",
            JobType = typeof(object),
            MaxAttempts = 3,
            CronExpression = "* * * * *",
            TimeZone = "UTC",
        };

        await store.UpsertRecurringJobAsync(recurringRegistration, DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None);
        var recurring = Assert.Single(await store.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None));
        _ = await store.TryMaterializeRecurringRunAsync(
            recurring,
            recurringRegistration,
            DateTimeOffset.UtcNow.AddMinutes(1),
            CancellationToken.None);

        var jobRuns = await query.GetRunsByJobNameAsync("send-email", 10, CancellationToken.None);
        Assert.Single(jobRuns);

        var enqueuedOnly = await query.GetEnqueuedRunsAsync(20, CancellationToken.None);
        Assert.Single(enqueuedOnly);
        Assert.Equal("send-email", enqueuedOnly[0].JobName);
    }
}
