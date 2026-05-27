using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using DurableStack.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.Events;

public sealed class IngestionEventSyncHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IngestionDurableStackEventSink? _sink;
    private readonly DurableStackOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IngestionEventSyncHostedService> _logger;
    private readonly Random _random = new();
    private int _sequence;

    public IngestionEventSyncHostedService(
        IEnumerable<IDurableStackEventSink> sinks,
        DurableStackOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<IngestionEventSyncHostedService> logger)
    {
        _sink = sinks.OfType<IngestionDurableStackEventSink>().FirstOrDefault();
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Eventing.TenantId) || string.IsNullOrWhiteSpace(_options.Eventing.ClientSecret))
        {
            _logger.LogInformation("DurableStack ingestion sync disabled because tenant credentials are not configured.");
            return;
        }

        if (_sink is null)
        {
            _logger.LogWarning("DurableStack ingestion sync disabled because ingestion sink registration is missing.");
            return;
        }

        var pending = new List<DurableStackEvent>();
        var flushInterval = _options.Eventing.IngestionFlushInterval;
        var maxBatchSize = Math.Clamp(_options.Eventing.IngestionMaxBatchSize, 1, 1000);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (pending.Count < maxBatchSize && _sink.Reader.TryRead(out var evt))
                {
                    pending.Add(evt);
                }

                if (pending.Count > 0)
                {
                    await FlushAsync(pending, stoppingToken);
                    pending.Clear();
                }

                await Task.Delay(flushInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DurableStack ingestion sync loop failure.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        if (pending.Count > 0)
        {
            try
            {
                await FlushAsync(pending, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to flush pending ingestion events during shutdown.");
            }
        }
    }

    private async Task FlushAsync(List<DurableStackEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var idempotencyKey = BuildIdempotencyKey();
        var request = new TelemetryBatchRequest
        {
            TenantId = _options.Eventing.TenantId,
            IdempotencyKey = idempotencyKey,
            ServiceName = _options.Eventing.ServiceName,
            EnvironmentName = null,
            Events = BuildDtos(events),
        };

        var payload = JsonSerializer.Serialize(request, JsonOptions);
        if (Encoding.UTF8.GetByteCount(payload) > _options.Eventing.IngestionMaxRequestBodyBytes)
        {
            _logger.LogWarning(
                "Skipping ingestion flush because payload exceeds max size. Count={Count} Bytes={Bytes} Max={MaxBytes}",
                events.Count,
                Encoding.UTF8.GetByteCount(payload),
                _options.Eventing.IngestionMaxRequestBodyBytes);
            return;
        }

        var posted = await PostWithRetryAsync(payload, idempotencyKey, events.Count, cancellationToken);
        if (!posted)
        {
            _logger.LogWarning(
                "DurableStack ingestion skipped batch after retry exhaustion. EventsDropped={EventCount}",
                events.Count);
        }
    }

    private async Task<bool> PostWithRetryAsync(string payload, string idempotencyKey, int eventCount, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(nameof(IngestionEventSyncHostedService));
        var baseUri = new Uri(_options.Eventing.IngestionApiBaseUrl, UriKind.Absolute);
        var endpoint = new Uri(baseUri, _options.Eventing.IngestionPath);

        var maxAttempts = Math.Clamp(_options.Eventing.IngestionMaxRetryAttempts, 1, 10);

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
                    return true;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogError(
                        "DurableStack ingestion authorization failed with status {StatusCode}. Event sync disabled for this cycle.",
                        (int)response.StatusCode);
                    return false;
                }

                if (!IsTransient(response.StatusCode))
                {
                    _logger.LogWarning(
                        "DurableStack ingestion rejected batch without retry. Status={StatusCode} EventCount={EventCount}",
                        (int)response.StatusCode,
                        eventCount);
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(ex, "DurableStack ingestion failed after retries. EventCount={EventCount}", eventCount);
                    return false;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogWarning(ex, "DurableStack ingestion timed out after retries. EventCount={EventCount}", eventCount);
                    return false;
                }
            }
            finally
            {
                response?.Dispose();
            }

            var delay = ComputeBackoffDelay(attempt);
            _logger.LogDebug(
                "Retrying DurableStack ingestion batch. Attempt={Attempt} DelayMs={DelayMs} EventCount={EventCount} IdempotencyKey={IdempotencyKey}",
                attempt,
                delay.TotalMilliseconds,
                eventCount,
                idempotencyKey);

            await Task.Delay(delay, cancellationToken);
        }

        return false;
    }

    private List<TelemetryEventDto> BuildDtos(List<DurableStackEvent> events)
    {
        var list = new List<TelemetryEventDto>(events.Count);
        var heartbeatEvents = new List<DurableStackEvent>();

        foreach (var evt in events)
        {
            if (string.Equals(evt.EventType, DurableStackEventTypes.WorkerHeartbeat, StringComparison.Ordinal))
            {
                heartbeatEvents.Add(evt);
                continue;
            }

            list.Add(new TelemetryEventDto
            {
                EventType = evt.EventType,
                EventVersion = evt.EventVersion,
                OccurredAtUtc = evt.OccurredAtUtc,
                RunId = evt.RunId,
                JobName = evt.JobName,
                Attempt = evt.Attempt,
                WorkerName = evt.WorkerName,
                DurationMs = evt.DurationMs,
                ErrorType = evt.ErrorType,
                ErrorMessage = evt.Message,
                PayloadJson = BuildPayloadJson(evt),
            });
        }

        if (heartbeatEvents.Count > 0)
        {
            var latest = heartbeatEvents.MaxBy(x => x.OccurredAtUtc)!;
            var first = heartbeatEvents.MinBy(x => x.OccurredAtUtc)!;

            var heartbeatPayload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["heartbeatCount"] = heartbeatEvents.Count,
                ["firstHeartbeatAtUtc"] = first.OccurredAtUtc,
                ["lastHeartbeatAtUtc"] = latest.OccurredAtUtc,
            }, JsonOptions);

            list.Add(new TelemetryEventDto
            {
                EventType = "worker_heartbeat_batch",
                EventVersion = latest.EventVersion,
                OccurredAtUtc = latest.OccurredAtUtc,
                WorkerName = latest.WorkerName,
                PayloadJson = heartbeatPayload,
            });
        }

        return list;
    }

    private static string BuildPayloadJson(DurableStackEvent evt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["message"] = evt.Message,
            ["errorType"] = evt.ErrorType,
            ["errorDetail"] = evt.ErrorDetail,
            ["durationMs"] = evt.DurationMs,
            ["retryAtUtc"] = evt.RetryAtUtc,
            ["traceId"] = evt.TraceId,
            ["spanId"] = evt.SpanId,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private string BuildIdempotencyKey()
    {
        var seq = Interlocked.Increment(ref _sequence);
        var utc = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var tenant = _options.Eventing.TenantId?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? "tenant";
        return $"{tenant}-{utc}-{seq}";
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
}
