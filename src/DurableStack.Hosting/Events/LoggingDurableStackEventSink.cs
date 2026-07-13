using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.Events;

/// <summary>
/// Event sink implementation that writes each worker event to <see cref="ILogger"/> at information
/// level, including the event type, run id, job name, attempt, and worker name. Register it with
/// <c>UseDurableStackLoggingEventSink</c>.
/// </summary>
public sealed class LoggingDurableStackEventSink : IDurableStackEventSink
{
    private readonly ILogger<LoggingDurableStackEventSink> _logger;

    /// <summary>
    /// Creates the sink with the logger that receives the event entries.
    /// </summary>
    /// <param name="logger">Logger the events are written to.</param>
    public LoggingDurableStackEventSink(ILogger<LoggingDurableStackEventSink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "DurableStack event {EventType} EventId={EventId} RunId={RunId} JobName={JobName} Attempt={Attempt} Worker={Worker}",
            @event.EventType,
            @event.EventId,
            @event.RunId,
            @event.JobName,
            @event.Attempt,
            @event.WorkerName);

        return Task.CompletedTask;
    }
}
