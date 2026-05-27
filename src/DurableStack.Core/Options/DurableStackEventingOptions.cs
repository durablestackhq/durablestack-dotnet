using System;

namespace DurableStack.Core.Options;

public sealed class DurableStackEventingOptions
{
    public string? TenantId { get; set; }

    public string? ClientSecret { get; set; }

    public string? Environment { get; set; }

    public string? ServiceName { get; set; }

    public string IngestionApiBaseUrl { get; set; } = "https://api.durablestack.com";

    public string IngestionPath { get; set; } = "/v1/events/batch";

    public int IngestionMaxBatchSize { get; set; } = 100;

    public int IngestionMaxRequestBodyBytes { get; set; } = 1_000_000;

    public int IngestionMaxRetryAttempts { get; set; } = 5;

    public TimeSpan IngestionFlushInterval { get; set; } = TimeSpan.FromSeconds(5);
}
