using System.Threading;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.Events;

public sealed class LoggingDurableStackEventSink : IDurableStackEventSink
{
    private readonly ILogger<LoggingDurableStackEventSink> _logger;

    public LoggingDurableStackEventSink(ILogger<LoggingDurableStackEventSink> logger)
    {
        _logger = logger;
    }

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
