using System;

namespace DurableStack.Core.Options;

/// <summary>
/// Settings for publishing job lifecycle events to the hosted DurableStack telemetry
/// platform: tenant credentials, ingestion endpoint, batching and flushing, and how much
/// error information is allowed to leave the process.
/// </summary>
public sealed class DurableStackEventingOptions
{
    private static readonly TimeSpan DefaultIngestionFlushInterval = TimeSpan.FromSeconds(5);
    private const int DefaultMaxErrorDetailLength = 4096;

    /// <summary>
    /// Tenant identifier stamped on every published event and used to authenticate against
    /// the ingestion API. Null (the default) leaves hosted eventing unconfigured.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Secret used together with <see cref="TenantId"/> to authenticate against the
    /// ingestion API. Supply it from a secret store rather than committed configuration.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Logical name of this service, stamped on every published event to distinguish
    /// multiple services under the same tenant. Null omits the field.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Base URL of the event ingestion API. Defaults to "https://api.durablestack.com";
    /// override to target a different environment.
    /// </summary>
    public string IngestionApiBaseUrl { get; set; } = "https://api.durablestack.com";

    /// <summary>
    /// Request path, relative to <see cref="IngestionApiBaseUrl"/>, that event batches are
    /// posted to. Defaults to "/v1/events/batch".
    /// </summary>
    public string IngestionPath { get; set; } = "/v1/events/batch";

    /// <summary>
    /// Maximum number of events sent in one ingestion request. Defaults to 100.
    /// </summary>
    public int IngestionMaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum serialized size in bytes of one ingestion request body; a batch is split
    /// sooner when it would exceed this. Defaults to 1,000,000 bytes.
    /// </summary>
    public int IngestionMaxRequestBodyBytes { get; set; } = 1_000_000;

    /// <summary>
    /// How many times a failed ingestion request is retried before its events are dropped.
    /// Defaults to 5. Event delivery is best-effort and never affects job outcomes.
    /// </summary>
    public int IngestionMaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// How often buffered events are flushed to the ingestion API even when a batch is not
    /// yet full. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan IngestionFlushInterval { get; set; } = DefaultIngestionFlushInterval;

    /// <summary>
    /// Configuration-binding alias for <see cref="IngestionFlushInterval"/>, expressed in
    /// seconds. Setting a zero or negative value silently resets the interval to the
    /// 5-second default.
    /// </summary>
    public double IngestionFlushIntervalSeconds
    {
        get => IngestionFlushInterval.TotalSeconds;
        set => IngestionFlushInterval = value > 0 ? TimeSpan.FromSeconds(value) : DefaultIngestionFlushInterval;
    }

    /// <summary>
    /// Whether exception messages and stack traces are included in published failure events.
    /// False by default: only the exception type name leaves the process, because messages
    /// and traces routinely contain sensitive values (connection details, file paths, SQL
    /// fragments, business data).
    /// </summary>
    public bool IncludeErrorDetail { get; set; } = false;

    /// <summary>
    /// Maximum number of characters of error message and stack trace sent per event when
    /// <see cref="IncludeErrorDetail"/> is true; longer text is truncated. Defaults to 4096.
    /// </summary>
    public int MaxErrorDetailLength { get; set; } = DefaultMaxErrorDetailLength;

    /// <summary>
    /// Returns the error-detail limit actually applied: <see cref="MaxErrorDetailLength"/>
    /// when positive, otherwise the 4096-character default.
    /// </summary>
    public int GetEffectiveMaxErrorDetailLength()
    {
        return MaxErrorDetailLength > 0 ? MaxErrorDetailLength : DefaultMaxErrorDetailLength;
    }
}
