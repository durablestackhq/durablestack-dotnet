using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DurableStack.AspNetCore.Events;

internal sealed class TelemetryBatchRequest
{
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("idempotencyKey")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; init; }

    [JsonPropertyName("events")]
    public required List<TelemetryEventDto> Events { get; init; }
}

internal sealed class TelemetryEventDto
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    [JsonPropertyName("eventVersion")]
    public required int EventVersion { get; init; }

    [JsonPropertyName("occurredAtUtc")]
    public required DateTimeOffset OccurredAtUtc { get; init; }

    [JsonPropertyName("runId")]
    public Guid? RunId { get; init; }

    [JsonPropertyName("jobName")]
    public string? JobName { get; init; }

    [JsonPropertyName("attempt")]
    public int? Attempt { get; init; }

    [JsonPropertyName("workerName")]
    public string? WorkerName { get; init; }

    [JsonPropertyName("durationMs")]
    public double? DurationMs { get; init; }

    [JsonPropertyName("errorType")]
    public string? ErrorType { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("payloadJson")]
    public string? PayloadJson { get; init; }
}

internal sealed class TelemetryBatchResponse
{
    [JsonPropertyName("acceptedCount")]
    public int AcceptedCount { get; init; }

    [JsonPropertyName("rejectedCount")]
    public int RejectedCount { get; init; }

    [JsonPropertyName("serverTimeUtc")]
    public DateTimeOffset ServerTimeUtc { get; init; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("isDuplicate")]
    public bool IsDuplicate { get; init; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}
