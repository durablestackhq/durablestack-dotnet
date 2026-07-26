using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.RuntimeControl;

/// <summary>
/// Background service that periodically syncs recurring-schedule snapshots and command
/// receipts with the hosted runtime-control API, then executes leased commands locally.
/// </summary>
public sealed class RuntimeControlSyncHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DurableStackOptions _options;
    private readonly IDurableScheduleAdminService _scheduleAdmin;
    private readonly IDurableJobStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RuntimeControlSyncHostedService> _logger;
    private readonly Uri _controlBaseUri;
    private readonly string _runtime;
    private readonly string _runtimeVersion;
    private readonly Random _random = new();

    /// <summary>
    /// Creates the runtime control sync service.
    /// </summary>
    /// <param name="options">Worker options containing runtime control settings and tenant credentials.</param>
    /// <param name="scheduleAdmin">Administrative API used to apply runtime commands to local schedules.</param>
    /// <param name="store">Job store used to read and write local command receipts and leases.</param>
    /// <param name="httpClientFactory">Factory used to create the HTTP client for control sync requests.</param>
    /// <param name="logger">Logger for sync-loop diagnostics.</param>
    public RuntimeControlSyncHostedService(
        DurableStackOptions options,
        IDurableScheduleAdminService scheduleAdmin,
        IDurableJobStore store,
        IHttpClientFactory httpClientFactory,
        ILogger<RuntimeControlSyncHostedService> logger)
    {
        _options = options;
        _scheduleAdmin = scheduleAdmin;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _controlBaseUri = ValidateControlBaseUrl(options.Eventing.IngestionApiBaseUrl);
        _runtime = RuntimeInformation.FrameworkDescription;
        _runtimeVersion = Environment.Version.ToString();
    }

    /// <summary>
    /// Runs the runtime-control sync loop until host shutdown.
    /// </summary>
    /// <param name="stoppingToken">Signals host shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Eventing.RuntimeControlEnabled)
        {
            _logger.LogInformation("DurableStack runtime control sync disabled by configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Eventing.TenantId) || string.IsNullOrWhiteSpace(_options.Eventing.ClientSecret))
        {
            _logger.LogInformation("DurableStack runtime control sync disabled because tenant credentials are not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
                await Task.Delay(_options.Eventing.RuntimeControlSyncInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack runtime control sync loop failure.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var schedules = await _scheduleAdmin.ListScheduledJobsAsync(includeDisabled: true, cancellationToken);
        var maxReceiptUpload = Math.Max(1, _options.Eventing.RuntimeControlMaxReceiptUpload);
        var receipts = await _store.GetRuntimeCommandReceiptsAsync(maxReceiptUpload, cancellationToken);

        var request = new RuntimeControlSyncRequest
        {
            TenantId = _options.Eventing.TenantId!,
            WorkerName = _options.WorkerName,
            Runtime = _runtime,
            RuntimeVersion = _runtimeVersion,
            SentAtUtc = nowUtc,
            SnapshotItems = BuildSnapshotItems(schedules, nowUtc),
            Receipts = BuildReceiptDtos(receipts),
        };

        var response = await PostSyncWithRetryAsync(request, cancellationToken);
        if (response is null)
        {
            return;
        }

        foreach (var receipt in receipts)
        {
            _ = await _store.MarkRuntimeCommandReceiptUploadedAsync(receipt.CommandId, cancellationToken);
        }

        await ProcessCommandsAsync(response.Commands, cancellationToken);
    }

    private async Task ProcessCommandsAsync(IReadOnlyList<RuntimeCommandEnvelopeDto> commands, CancellationToken cancellationToken)
    {
        if (commands.Count == 0)
        {
            return;
        }

        var leaseDuration = _options.Eventing.RuntimeControlCommandLeaseDuration;
        var workerName = _options.WorkerName;

        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.CommandId))
            {
                continue;
            }

            if (command.ExpiresAtUtc.HasValue && command.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
            {
                continue;
            }

            var commandId = command.CommandId.Trim();
            var leased = await _store.TryLeaseRuntimeCommandReceiptAsync(commandId, workerName, leaseDuration, cancellationToken);
            if (!leased)
            {
                continue;
            }

            var acknowledgedAt = DateTimeOffset.UtcNow;
            var acknowledged = await _store.MarkRuntimeCommandAcknowledgedAsync(commandId, workerName, acknowledgedAt, cancellationToken);
            if (!acknowledged)
            {
                continue;
            }

            try
            {
                var execution = await ExecuteCommandAsync(command, cancellationToken);
                if (execution.Success)
                {
                    _ = await _store.MarkRuntimeCommandSucceededAsync(
                        commandId,
                        workerName,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        execution.RunId,
                        cancellationToken);
                }
                else
                {
                    _ = await _store.MarkRuntimeCommandFailedAsync(
                        commandId,
                        workerName,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        execution.ErrorCode,
                        execution.ErrorMessage,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DurableStack runtime control command execution failed. CommandId={CommandId} CommandType={CommandType}", commandId, command.CommandType);
                _ = await _store.MarkRuntimeCommandFailedAsync(
                    commandId,
                    workerName,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    "runtime_exception",
                    Truncate(ex.Message, 2000),
                    cancellationToken);
            }
        }
    }

    private async Task<CommandExecutionResult> ExecuteCommandAsync(RuntimeCommandEnvelopeDto command, CancellationToken cancellationToken)
    {
        var commandType = command.CommandType?.Trim();
        if (string.IsNullOrWhiteSpace(commandType))
        {
            return CommandExecutionResult.Failed("invalid_command_type", "CommandType is required.");
        }

        switch (commandType)
        {
            case "set_schedule_enabled":
            {
                var payload = DeserializePayload<SetScheduleEnabledPayload>(command.PayloadJson);
                if (payload is null || string.IsNullOrWhiteSpace(payload.JobName))
                {
                    return CommandExecutionResult.Failed("invalid_payload", "Payload must include jobName and enabled.");
                }

                var updated = await _scheduleAdmin.SetScheduledJobEnabledAsync(payload.JobName.Trim(), payload.Enabled, cancellationToken);
                return updated
                    ? CommandExecutionResult.Succeeded(null)
                    : CommandExecutionResult.Failed("schedule_not_found", $"Scheduled job '{payload.JobName.Trim()}' was not found.");
            }
            case "run_schedule_now":
            {
                var payload = DeserializePayload<RunScheduleNowPayload>(command.PayloadJson);
                if (payload is null || string.IsNullOrWhiteSpace(payload.JobName))
                {
                    return CommandExecutionResult.Failed("invalid_payload", "Payload must include jobName.");
                }

                try
                {
                    var runId = await _scheduleAdmin.RunScheduledJobNowAsync(payload.JobName.Trim(), cancellationToken);
                    return runId.HasValue
                        ? CommandExecutionResult.Succeeded(runId)
                        : CommandExecutionResult.Failed("schedule_not_found", $"Scheduled job '{payload.JobName.Trim()}' was not found.");
                }
                catch (ScheduledJobRunBlockedException ex)
                {
                    return CommandExecutionResult.Failed("schedule_run_blocked", Truncate(ex.Message, 2000));
                }
            }
            case "update_schedule_cron":
            {
                var payload = DeserializePayload<UpdateScheduleCronPayload>(command.PayloadJson);
                if (payload is null
                    || string.IsNullOrWhiteSpace(payload.JobName)
                    || string.IsNullOrWhiteSpace(payload.CronExpression)
                    || string.IsNullOrWhiteSpace(payload.TimeZone))
                {
                    return CommandExecutionResult.Failed("invalid_payload", "Payload must include jobName, cronExpression, and timeZone.");
                }

                var updated = await _scheduleAdmin.UpdateScheduledJobCronAsync(
                    payload.JobName.Trim(),
                    payload.CronExpression.Trim(),
                    payload.TimeZone.Trim(),
                    cancellationToken);

                return updated
                    ? CommandExecutionResult.Succeeded(null)
                    : CommandExecutionResult.Failed("schedule_not_found", $"Scheduled job '{payload.JobName.Trim()}' was not found.");
            }
            default:
                return CommandExecutionResult.Failed("unsupported_command_type", $"Unsupported command type '{commandType}'.");
        }
    }

    private async Task<RuntimeControlSyncResponse?> PostSyncWithRetryAsync(RuntimeControlSyncRequest requestPayload, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(requestPayload, JsonOptions);
        var maxAttempts = Math.Clamp(_options.Eventing.IngestionMaxRetryAttempts, 1, 10);
        using var client = _httpClientFactory.CreateClient(nameof(RuntimeControlSyncHostedService));
        var endpoint = new Uri(_controlBaseUri, _options.Eventing.RuntimeControlSyncPath);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("X-DurableStack-TenantId", _options.Eventing.TenantId);
            request.Headers.Add("X-DurableStack-ClientSecret", _options.Eventing.ClientSecret);
            request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var syncResponse = JsonSerializer.Deserialize<RuntimeControlSyncResponse>(content, JsonOptions);
                    return syncResponse ?? new RuntimeControlSyncResponse();
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogError(
                        "DurableStack runtime control authorization failed with status {StatusCode}. Sync skipped for this cycle.",
                        (int)response.StatusCode);
                    return null;
                }

                if (!IsTransient(response.StatusCode))
                {
                    _logger.LogWarning(
                        "DurableStack runtime control sync rejected without retry. Status={StatusCode}",
                        (int)response.StatusCode);
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(ex, "DurableStack runtime control sync failed after retries.");
                    return null;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(ex, "DurableStack runtime control sync timed out after retries.");
                    return null;
                }
            }
            finally
            {
                response?.Dispose();
            }

            var delay = ComputeBackoffDelay(attempt);
            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }

    private static Uri ValidateControlBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(
                $"Eventing.IngestionApiBaseUrl '{baseUrl}' is not a valid absolute URL.",
                nameof(baseUrl));
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            return uri;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            throw new ArgumentException(
                $"Eventing.IngestionApiBaseUrl '{baseUrl}' must use https (the control sync request carries tenant credentials); http is only permitted for loopback addresses.",
                nameof(baseUrl));
        }

        throw new ArgumentException(
            $"Eventing.IngestionApiBaseUrl '{baseUrl}' must be an absolute http(s) URL.",
            nameof(baseUrl));
    }

    private static List<RuntimeJobSnapshotItemDto> BuildSnapshotItems(IReadOnlyList<Core.Models.RecurringJobState> schedules, DateTimeOffset lastSeenAtUtc)
    {
        var list = new List<RuntimeJobSnapshotItemDto>(schedules.Count);
        foreach (var schedule in schedules)
        {
            list.Add(new RuntimeJobSnapshotItemDto
            {
                JobName = schedule.JobName,
                JobType = schedule.JobType,
                CronExpression = schedule.CronExpression,
                TimeZone = schedule.TimeZone,
                Enabled = schedule.Enabled,
                NextRunAtUtc = schedule.NextRunAtUtc,
                MaxAttempts = schedule.MaxAttempts,
                AllowConcurrentRuns = schedule.AllowConcurrentRuns,
                LastSeenAtUtc = lastSeenAtUtc,
            });
        }

        return list;
    }

    private static List<RuntimeCommandReceiptDto> BuildReceiptDtos(IReadOnlyList<Core.Models.RuntimeCommandReceiptRecord> receipts)
    {
        var list = new List<RuntimeCommandReceiptDto>(receipts.Count);
        foreach (var receipt in receipts)
        {
            list.Add(new RuntimeCommandReceiptDto
            {
                CommandId = receipt.CommandId,
                Status = receipt.Status,
                RecordedAtUtc = receipt.RecordedAtUtc,
                CompletedAtUtc = receipt.CompletedAtUtc,
                RunId = receipt.RunId,
                ErrorCode = receipt.ErrorCode,
                ErrorMessage = receipt.ErrorMessage,
            });
        }

        return list;
    }

    private static TPayload? DeserializePayload<TPayload>(string payloadJson)
        where TPayload : class
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TPayload>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == (HttpStatusCode)429
            || code >= 500;
    }

    private TimeSpan ComputeBackoffDelay(int attempt)
    {
        var baseDelayMs = Math.Min(30_000, 500 * (int)Math.Pow(2, Math.Max(0, attempt - 1)));
        var jitter = _random.Next(0, 250);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitter);
    }

    private static string Truncate(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
        {
            return message;
        }

        return message[..maxLength];
    }

    private sealed class SetScheduleEnabledPayload
    {
        public string? JobName { get; set; }

        public bool Enabled { get; set; }
    }

    private sealed class RunScheduleNowPayload
    {
        public string? JobName { get; set; }
    }

    private sealed class UpdateScheduleCronPayload
    {
        public string? JobName { get; set; }

        public string? CronExpression { get; set; }

        public string? TimeZone { get; set; }
    }

    private readonly record struct CommandExecutionResult(bool Success, Guid? RunId, string? ErrorCode, string? ErrorMessage)
    {
        public static CommandExecutionResult Succeeded(Guid? runId) => new(true, runId, null, null);

        public static CommandExecutionResult Failed(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
    }
}
