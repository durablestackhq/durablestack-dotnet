using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using Microsoft.Extensions.Logging;

namespace DurableStack.Hosting.Events;

/// <summary>
/// Event sink implementation that buffers worker events in a bounded in-memory channel (capacity 5000)
/// for <see cref="IngestionEventSyncHostedService"/> to batch and forward to the hosted observability API.
/// When the buffer is full, new events are dropped and a warning is logged; publishing never blocks job execution.
/// </summary>
public sealed class IngestionDurableStackEventSink : IDurableStackEventSink
{
    private readonly Channel<DurableStackEvent> _channel;
    private readonly ILogger<IngestionDurableStackEventSink> _logger;

    /// <summary>
    /// Creates the sink and its bounded event buffer.
    /// </summary>
    /// <param name="logger">Logger used to warn when events are dropped because the buffer is full.</param>
    public IngestionDurableStackEventSink(ILogger<IngestionDurableStackEventSink> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<DurableStackEvent>(new BoundedChannelOptions(5000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    }

    /// <summary>
    /// Reader side of the buffer, consumed by <see cref="IngestionEventSyncHostedService"/> to drain
    /// events into ingestion batches.
    /// </summary>
    public ChannelReader<DurableStackEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public Task PublishAsync(DurableStackEvent @event, CancellationToken cancellationToken = default)
    {
        if (!_channel.Writer.TryWrite(@event))
        {
            _logger.LogWarning(
                "DurableStack ingestion queue is full. Dropping event {EventType} EventId={EventId}.",
                @event.EventType,
                @event.EventId);
        }

        return Task.CompletedTask;
    }
}
