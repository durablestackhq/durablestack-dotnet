using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Models;

namespace DurableStack.Core.Abstractions;

public interface IDurableScheduleAdminService
{
    Task<IReadOnlyList<RecurringJobState>> ListScheduledJobsAsync(bool includeDisabled = true, CancellationToken cancellationToken = default);

    Task<bool> SetScheduledJobEnabledAsync(string jobName, bool enabled, CancellationToken cancellationToken = default);

    Task<bool> UpdateScheduledJobCronAsync(string jobName, string cronExpression, string timeZone = "UTC", CancellationToken cancellationToken = default);

    Task<Guid?> RunScheduledJobNowAsync(string jobName, CancellationToken cancellationToken = default);
}
