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
        string? errorMessage = null,
        string? errorDetail = null)
    {
        var activity = Activity.Current;

        return new DurableStackEvent
        {
            EventType = eventType,
            EventVersion = DurableStackEventTypes.CurrentVersion,
            RunId = run?.Id,
            JobName = run?.JobName,
            Attempt = run?.Attempt,
            MaxAttempts = run?.MaxAttempts,
            WorkerName = workerName ?? _options.WorkerName,
            TenantId = _options.Eventing.TenantId,
            ServiceName = _options.Eventing.ServiceName,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            DurationMs = durationMs,
            RetryAtUtc = retryAtUtc,
            ErrorType = errorType,
            ErrorMessage = SanitizeSensitiveErrorText(errorMessage),
            ErrorDetail = SanitizeSensitiveErrorText(errorDetail),
            Message = message,
        };
    }

    private string? SanitizeSensitiveErrorText(string? text)
    {
        // Exception messages and stack traces routinely contain sensitive values
        // (connection details, file paths, SQL fragments, business data). Unless the
        // deployment opts in, only the exception type leaves the process.
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!_options.Eventing.IncludeErrorDetail)
        {
            return null;
        }

        var maxLength = _options.Eventing.GetEffectiveMaxErrorDetailLength();
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }
}
