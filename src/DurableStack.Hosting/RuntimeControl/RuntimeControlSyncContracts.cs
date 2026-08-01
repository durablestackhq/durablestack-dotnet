using System;
using System.Collections.Generic;

namespace DurableStack.Hosting.RuntimeControl;

internal sealed class RuntimeControlSyncRequest
{
    public string TenantId { get; set; } = string.Empty;

    public string? WorkerName { get; set; }

    public string? Runtime { get; set; }

    public string? RuntimeVersion { get; set; }

    public DateTimeOffset SentAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<RuntimeJobSnapshotItemDto> SnapshotItems { get; set; } = [];

    public List<RuntimeCommandReceiptDto> Receipts { get; set; } = [];
}

internal sealed class RuntimeControlSyncResponse
{
    public DateTimeOffset ServerTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<RuntimeCommandEnvelopeDto> Commands { get; set; } = [];
}

internal sealed class RuntimeCommandEnvelopeDto
{
    public string CommandId { get; set; } = string.Empty;

    public string CommandType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }
}

internal sealed class RuntimeJobSnapshotItemDto
{
    public string JobName { get; set; } = string.Empty;

    public string? JobType { get; set; }

    public string CronExpression { get; set; } = string.Empty;

    public string TimeZone { get; set; } = "UTC";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset NextRunAtUtc { get; set; }

    public int MaxAttempts { get; set; }

    public bool AllowConcurrentRuns { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class RuntimeCommandReceiptDto
{
    public string CommandId { get; set; } = string.Empty;

    public string Status { get; set; } = "acknowledged";

    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid? RunId { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
