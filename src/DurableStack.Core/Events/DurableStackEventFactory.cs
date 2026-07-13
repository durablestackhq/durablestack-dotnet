using System.Diagnostics;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Core.Events;

/// <summary>
/// Builds <see cref="DurableStackEvent"/> instances with the common fields stamped from
/// configuration and ambient state: worker name, tenant, service name, and the current
/// trace/span ids. Redacts <c>ErrorMessage</c>/<c>ErrorDetail</c> unless
/// <c>Eventing.IncludeErrorDetail</c> is enabled, and truncates them to the configured
/// maximum length when it is.
/// </summary>
public sealed class DurableStackEventFactory
{
    private readonly DurableStackOptions _options;

    /// <summary>
    /// Creates a factory that reads worker, tenant, and redaction settings from
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Configuration supplying the worker name and eventing settings applied to every event.</param>
    public DurableStackEventFactory(DurableStackOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Creates an event of the given type, copying run identity fields from
    /// <paramref name="run"/> when provided and stamping tenant, service, worker, and
    /// current trace context. Error text is redacted or truncated per the eventing options.
    /// </summary>
    /// <param name="eventType">One of the <see cref="DurableStackEventTypes"/> constants.</param>
    /// <param name="run">Run whose id, job name, and attempt counters are copied onto the event, if any.</param>
    /// <param name="message">Optional free-form informational text.</param>
    /// <param name="workerName">Overrides the configured worker name when set.</param>
    /// <param name="durationMs">Elapsed execution time in milliseconds, for completion events.</param>
    /// <param name="retryAtUtc">UTC time of the next scheduled retry, for retry-related events.</param>
    /// <param name="errorType">Full type name of the causing exception; emitted as-is.</param>
    /// <param name="errorMessage">Exception message; dropped unless error detail is enabled in options.</param>
    /// <param name="errorDetail">Full exception detail; dropped unless error detail is enabled, truncated otherwise.</param>
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
