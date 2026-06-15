using System.Diagnostics;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Core.Events;

public sealed class DurableStackEventFactory
{
    private readonly DurableStackOptions _options;

    public DurableStackEventFactory(DurableStackOptions options)
    {
        _options = options;
    }

    public DurableStackEvent Create(
        string eventType,
        JobRunRecord? run = null,
        string? message = null,
        string? workerName = null,
        double? durationMs = null,
        DateTimeOffset? retryAtUtc = null,
        string? errorType = null,
        string? errorDetail = null)
    {
        var activity = Activity.Current;
        var sanitizedErrorDetail = SanitizeErrorDetail(errorDetail);

        return new DurableStackEvent
        {
            EventType = eventType,
            EventVersion = DurableStackEventTypes.CurrentVersion,
            RunId = run?.Id,
            JobName = run?.JobName,
            Attempt = run?.Attempt,
            WorkerName = workerName ?? _options.WorkerName,
            TenantId = _options.Eventing.TenantId,
            Environment = _options.Eventing.Environment,
            ServiceName = _options.Eventing.ServiceName,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            DurationMs = durationMs,
            RetryAtUtc = retryAtUtc,
            ErrorType = errorType,
            ErrorDetail = sanitizedErrorDetail,
            Message = message,
        };
    }

    private string? SanitizeErrorDetail(string? errorDetail)
    {
        if (string.IsNullOrWhiteSpace(errorDetail))
        {
            return errorDetail;
        }

        if (!_options.Eventing.IncludeErrorDetail)
        {
            return null;
        }

        var maxLength = _options.Eventing.GetEffectiveMaxErrorDetailLength();
        if (errorDetail.Length <= maxLength)
        {
            return errorDetail;
        }

        return errorDetail[..maxLength];
    }
}
