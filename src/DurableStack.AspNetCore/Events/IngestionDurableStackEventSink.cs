using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Events;
using Microsoft.Extensions.Logging;

namespace DurableStack.AspNetCore.Events;

public sealed class IngestionDurableStackEventSink : IDurableStackEventSink
{
    private readonly Channel<DurableStackEvent> _channel;
    private readonly ILogger<IngestionDurableStackEventSink> _logger;

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

    public ChannelReader<DurableStackEvent> Reader => _channel.Reader;

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
