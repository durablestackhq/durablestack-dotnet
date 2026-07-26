using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Models;
using DurableStack.Core.Options;
using DurableStack.Hosting.RuntimeControl;
using Microsoft.Extensions.Logging.Abstractions;

namespace DurableStack.Tests;

public sealed class RuntimeControlSyncHostedServiceTests
{
    [Fact]
    public async Task Sync_executes_supported_commands_and_uploads_receipts()
    {
        var options = new DurableStackOptions();
        options.Eventing.TenantId = "tenant-alpha";
        options.Eventing.ClientSecret = "secret-alpha";
        options.Eventing.IngestionApiBaseUrl = "https://example.test";
        options.Eventing.RuntimeControlSyncInterval = TimeSpan.FromMilliseconds(20);
        options.Eventing.RuntimeControlCommandLeaseDuration = TimeSpan.FromSeconds(10);

        var store = new FakeRuntimeControlStore
        {
            Schedules =
            [
                new RecurringJobState
                {
                    JobName = "heartbeat",
                    JobType = "HeartbeatJob",
                    CronExpression = "*/5 * * * *",
                    TimeZone = "UTC",
                    Enabled = true,
                    MaxAttempts = 3,
                    AllowConcurrentRuns = false,
                    NextRunAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                },
            ],
            PendingReceipts =
            [
                new RuntimeCommandReceiptRecord
                {
                    CommandId = "receipt-1",
                    Status = "succeeded",
                    RunId = Guid.NewGuid(),
                    RecordedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                },
            ],
        };

        var admin = new FakeScheduleAdminService
        {
            ScheduledJobs = store.Schedules,
            RunNowResult = Guid.NewGuid(),
        };

        var response = new RuntimeControlResponse
        {
            Commands =
            [
                new RuntimeControlCommandEnvelope
                {
                    CommandId = "cmd-enable",
                    CommandType = "set_schedule_enabled",
                    PayloadJson = "{\"jobName\":\"heartbeat\",\"enabled\":false}",
                },
                new RuntimeControlCommandEnvelope
                {
                    CommandId = "cmd-run",
                    CommandType = "run_schedule_now",
                    PayloadJson = "{\"jobName\":\"heartbeat\"}",
                },
                new RuntimeControlCommandEnvelope
                {
                    CommandId = "cmd-cron",
                    CommandType = "update_schedule_cron",
                    PayloadJson = "{\"jobName\":\"heartbeat\",\"cronExpression\":\"0 * * * *\",\"timeZone\":\"UTC\"}",
                },
            ],
        };

        var handler = new RuntimeControlTestHttpMessageHandler(response);
        var service = new RuntimeControlSyncHostedService(
            options,
            admin,
            store,
            new FixedHttpClientFactory(new HttpClient(handler)),
            NullLogger<RuntimeControlSyncHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await handler.WaitForAtLeastRequestsAsync(1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("tenant-alpha", handler.LastRequestPayload?.TenantId);
        Assert.NotNull(handler.LastRequestPayload);
        Assert.Single(handler.LastRequestPayload!.SnapshotItems);
        Assert.Single(handler.LastRequestPayload.Receipts);

        Assert.Contains(store.UploadedReceiptIds, x => x == "receipt-1");
        Assert.Contains(admin.EnableCalls, x => x.JobName == "heartbeat" && !x.Enabled);
        Assert.Contains(admin.CronCalls, x => x.JobName == "heartbeat" && x.CronExpression == "0 * * * *" && x.TimeZone == "UTC");
        Assert.Contains(admin.RunNowCalls, x => x == "heartbeat");

        Assert.Contains(store.SucceededCommandIds, x => x == "cmd-enable");
        Assert.Contains(store.SucceededCommandIds, x => x == "cmd-cron");
        Assert.Contains(store.SucceededCommandIds, x => x == "cmd-run");
    }

    [Fact]
    public async Task Sync_marks_invalid_payload_as_failed_receipt()
    {
        var options = new DurableStackOptions();
        options.Eventing.TenantId = "tenant-alpha";
        options.Eventing.ClientSecret = "secret-alpha";
        options.Eventing.IngestionApiBaseUrl = "https://example.test";
        options.Eventing.RuntimeControlSyncInterval = TimeSpan.FromMilliseconds(20);

        var store = new FakeRuntimeControlStore();
        var admin = new FakeScheduleAdminService();

        var response = new RuntimeControlResponse
        {
            Commands =
            [
                new RuntimeControlCommandEnvelope
                {
                    CommandId = "cmd-bad",
                    CommandType = "set_schedule_enabled",
                    PayloadJson = "{}",
                },
            ],
        };

        var handler = new RuntimeControlTestHttpMessageHandler(response);
        var service = new RuntimeControlSyncHostedService(
            options,
            admin,
            store,
            new FixedHttpClientFactory(new HttpClient(handler)),
            NullLogger<RuntimeControlSyncHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await handler.WaitForAtLeastRequestsAsync(1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(store.FailedCommands, x => x.CommandId == "cmd-bad" && x.ErrorCode == "invalid_payload");
    }

    [Fact]
    public async Task Sync_uploads_command_receipt_on_following_cycle()
    {
        var options = new DurableStackOptions();
        options.Eventing.TenantId = "tenant-alpha";
        options.Eventing.ClientSecret = "secret-alpha";
        options.Eventing.IngestionApiBaseUrl = "https://example.test";
        options.Eventing.RuntimeControlSyncInterval = TimeSpan.FromMilliseconds(20);

        var store = new FakeRuntimeControlStore();
        var admin = new FakeScheduleAdminService
        {
            RunNowResult = Guid.NewGuid(),
        };

        var firstHandler = new RuntimeControlTestHttpMessageHandler(new RuntimeControlResponse
        {
            Commands =
            [
                new RuntimeControlCommandEnvelope
                {
                    CommandId = "cmd-cycle",
                    CommandType = "run_schedule_now",
                    PayloadJson = "{\"jobName\":\"heartbeat\"}",
                },
            ],
        });

        var firstService = new RuntimeControlSyncHostedService(
            options,
            admin,
            store,
            new FixedHttpClientFactory(new HttpClient(firstHandler)),
            NullLogger<RuntimeControlSyncHostedService>.Instance);

        await firstService.StartAsync(CancellationToken.None);
        await firstHandler.WaitForAtLeastRequestsAsync(1, TimeSpan.FromSeconds(5));
        await firstService.StopAsync(CancellationToken.None);

        var secondHandler = new RuntimeControlTestHttpMessageHandler(new RuntimeControlResponse());
        var secondService = new RuntimeControlSyncHostedService(
            options,
            admin,
            store,
            new FixedHttpClientFactory(new HttpClient(secondHandler)),
            NullLogger<RuntimeControlSyncHostedService>.Instance);

        await secondService.StartAsync(CancellationToken.None);
        await secondHandler.WaitForAtLeastRequestsAsync(1, TimeSpan.FromSeconds(5));
        await secondService.StopAsync(CancellationToken.None);

        Assert.NotEmpty(firstHandler.RequestPayloads);
        var firstPayload = firstHandler.RequestPayloads[0];
        Assert.DoesNotContain(firstPayload.Receipts, x => x.CommandId == "cmd-cycle");
        Assert.Contains(secondHandler.RequestPayloads, x => x.Receipts.Exists(r => r.CommandId == "cmd-cycle"));
    }

    private sealed class RuntimeControlTestHttpMessageHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly Func<int, RuntimeControlResponse> _responseFactory;
        private int _requestCount;

        public RuntimeControlTestHttpMessageHandler(RuntimeControlResponse response)
            : this(_ => response)
        {
        }

        public RuntimeControlTestHttpMessageHandler(Func<int, RuntimeControlResponse> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public RuntimeControlRequestPayload? LastRequestPayload { get; private set; }

        public List<RuntimeControlRequestPayload> RequestPayloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var currentRequestCount = Interlocked.Increment(ref _requestCount);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var payload = JsonSerializer.Deserialize<RuntimeControlRequestPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            lock (_gate)
            {
                LastRequestPayload = payload;
                if (payload is not null)
                {
                    RequestPayloads.Add(payload);
                }
            }

            var content = JsonSerializer.Serialize(_responseFactory(currentRequestCount), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }

        public async Task WaitForAtLeastRequestsAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            while (Volatile.Read(ref _requestCount) < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(Volatile.Read(ref _requestCount) >= count, $"Expected at least {count} runtime control requests.");
        }

        public async Task WaitForReceiptInAnyRequestAsync(string commandId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (RequestPayloads.Exists(x => x.Receipts.Exists(r => string.Equals(r.CommandId, commandId, StringComparison.Ordinal))))
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            Assert.Fail($"Expected to observe receipt '{commandId}' in a subsequent runtime control request.");
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class FakeScheduleAdminService : IDurableScheduleAdminService
    {
        public IReadOnlyList<RecurringJobState> ScheduledJobs { get; set; } = [];

        public Guid? RunNowResult { get; set; }

        public List<(string JobName, bool Enabled)> EnableCalls { get; } = [];

        public List<(string JobName, string CronExpression, string TimeZone)> CronCalls { get; } = [];

        public List<string> RunNowCalls { get; } = [];

        public Task<IReadOnlyList<RecurringJobState>> ListScheduledJobsAsync(bool includeDisabled = true, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ScheduledJobs);
        }

        public Task<bool> SetScheduledJobEnabledAsync(string jobName, bool enabled, CancellationToken cancellationToken = default)
        {
            EnableCalls.Add((jobName, enabled));
            return Task.FromResult(true);
        }

        public Task<bool> UpdateScheduledJobCronAsync(string jobName, string cronExpression, string timeZone = "UTC", CancellationToken cancellationToken = default)
        {
            CronCalls.Add((jobName, cronExpression, timeZone));
            return Task.FromResult(true);
        }

        public Task<Guid?> RunScheduledJobNowAsync(string jobName, CancellationToken cancellationToken = default)
        {
            RunNowCalls.Add(jobName);
            return Task.FromResult(RunNowResult);
        }
    }

    private sealed class FakeRuntimeControlStore : IDurableJobStore
    {
        public List<RecurringJobState> Schedules { get; set; } = [];

        public List<RuntimeCommandReceiptRecord> PendingReceipts { get; set; } = [];

        public List<string> UploadedReceiptIds { get; } = [];

        public List<string> SucceededCommandIds { get; } = [];

        public List<(string CommandId, string? ErrorCode, string? ErrorMessage)> FailedCommands { get; } = [];

        private readonly HashSet<string> _activeLeases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeCommandReceiptRecord> _generatedReceipts = new(StringComparer.Ordinal);
        private readonly HashSet<string> _uploadedReceiptIds = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<RecurringJobState>> GetRecurringJobsAsync(bool includeDisabled, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RecurringJobState>>(Schedules);
        }

        public Task<IReadOnlyList<RuntimeCommandReceiptRecord>> GetRuntimeCommandReceiptsAsync(int take, CancellationToken cancellationToken)
        {
            var list = new List<RuntimeCommandReceiptRecord>();

            foreach (var receipt in PendingReceipts)
            {
                if (!_uploadedReceiptIds.Contains(receipt.CommandId))
                {
                    list.Add(receipt);
                }
            }

            foreach (var receipt in _generatedReceipts.Values)
            {
                if (!_uploadedReceiptIds.Contains(receipt.CommandId))
                {
                    list.Add(receipt);
                }
            }

            return Task.FromResult<IReadOnlyList<RuntimeCommandReceiptRecord>>(list);
        }

        public Task<bool> TryLeaseRuntimeCommandReceiptAsync(string commandId, string workerName, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            return Task.FromResult(_activeLeases.Add(commandId));
        }

        public Task<bool> MarkRuntimeCommandAcknowledgedAsync(string commandId, string workerName, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(_activeLeases.Contains(commandId));
        }

        public Task<bool> MarkRuntimeCommandSucceededAsync(string commandId, string workerName, DateTimeOffset recordedAtUtc, DateTimeOffset completedAtUtc, Guid? runId, CancellationToken cancellationToken)
        {
            if (!_activeLeases.Remove(commandId))
            {
                return Task.FromResult(false);
            }

            SucceededCommandIds.Add(commandId);
            _generatedReceipts[commandId] = new RuntimeCommandReceiptRecord
            {
                CommandId = commandId,
                Status = "succeeded",
                RunId = runId,
                RecordedAtUtc = recordedAtUtc,
                CompletedAtUtc = completedAtUtc,
            };
            return Task.FromResult(true);
        }

        public Task<bool> MarkRuntimeCommandFailedAsync(string commandId, string workerName, DateTimeOffset recordedAtUtc, DateTimeOffset completedAtUtc, string? errorCode, string? errorMessage, CancellationToken cancellationToken)
        {
            if (!_activeLeases.Remove(commandId))
            {
                return Task.FromResult(false);
            }

            FailedCommands.Add((commandId, errorCode, errorMessage));
            _generatedReceipts[commandId] = new RuntimeCommandReceiptRecord
            {
                CommandId = commandId,
                Status = "failed",
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                RecordedAtUtc = recordedAtUtc,
                CompletedAtUtc = completedAtUtc,
            };
            return Task.FromResult(true);
        }

        public Task<bool> MarkRuntimeCommandReceiptUploadedAsync(string commandId, CancellationToken cancellationToken)
        {
            UploadedReceiptIds.Add(commandId);
            _uploadedReceiptIds.Add(commandId);
            return Task.FromResult(true);
        }

        public Task<Guid> EnqueueAsync(string jobName, string jobType, string? payloadJson, DateTimeOffset scheduledForUtc, int maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> ClaimDueRunsAsync(string workerName, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkSucceededAsync(Guid runId, string workerName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkFailedAsync(Guid runId, string workerName, Exception exception, bool retry, DateTimeOffset? retryAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> GetRecentRunsAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> GetRunsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> GetRunsByJobNameAsync(string jobName, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> GetRunsByStatusAsync(string status, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunRecord>> GetEnqueuedRunsAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> TryEnqueueIfNoActiveRunAsync(string jobName, string jobType, string? payloadJson, DateTimeOffset scheduledForUtc, int maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetRecurringJobEnabledAsync(string jobName, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateRecurringJobScheduleAsync(string jobName, string cronExpression, string timeZone, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoricalRunsAsync(DateTimeOffset completedBeforeUtc, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertRecurringJobAsync(DurableJobRegistration registration, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RecurringJobState>> GetDueRecurringJobsAsync(DateTimeOffset nowUtc, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRecurringNextRunAsync(string jobName, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMaterializeRecurringRunAsync(RecurringJobState recurring, DurableJobRegistration registration, DateTimeOffset nextRunAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExtendLeaseAsync(Guid runId, string workerName, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RuntimeControlResponse
    {
        public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

        public List<RuntimeControlCommandEnvelope> Commands { get; set; } = [];
    }

    private sealed class RuntimeControlCommandEnvelope
    {
        public string CommandId { get; set; } = string.Empty;

        public string CommandType { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = "{}";

        public DateTimeOffset IssuedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? ExpiresAtUtc { get; set; }
    }

    private sealed class RuntimeControlRequestPayload
    {
        public string TenantId { get; set; } = string.Empty;

        public List<RuntimeControlSnapshotItem> SnapshotItems { get; set; } = [];

        public List<RuntimeControlReceiptItem> Receipts { get; set; } = [];
    }

    private sealed class RuntimeControlSnapshotItem
    {
        public string JobName { get; set; } = string.Empty;
    }

    private sealed class RuntimeControlReceiptItem
    {
        public string CommandId { get; set; } = string.Empty;
    }
}
