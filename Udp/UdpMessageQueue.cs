using System.Collections.Concurrent;

namespace SRF.Network.Udp;

/// <summary>
/// Singleton service that holds the UDP send queue and the underlying multicast client.
/// Consumers inject <see cref="IUdpMessageQueue"/> to enqueue messages. The
/// <see cref="Hosting.UdpConnectionManager"/> background service drains this queue.
/// </summary>
public sealed class UdpMessageQueue : IUdpMessageQueue, IDisposable
{
    private readonly BlockingCollection<UdpQueueItem> _queue = new();
    private readonly ILogger<UdpMessageQueue> _logger;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <summary>
    /// The underlying UDP multicast client used for actual transport.
    /// Consumers who need to receive messages or check connection status
    /// should inject <see cref="IUdpMulticastClient"/> directly.
    /// </summary>
    public IUdpMulticastClient Client { get; }

    public int QueuedMessageCount => _queue.Count;
    public bool IsCompleted => _queue.IsCompleted;

    public UdpMessageQueue(IUdpMulticastClient client, ILogger<UdpMessageQueue> logger, TimeProvider timeProvider)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public UdpQueueItem Enqueue(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (_queue.IsAddingCompleted)
            throw new InvalidOperationException("Cannot enqueue: the message queue has been shut down.");

        var item = new UdpQueueItem(data, _timeProvider.GetUtcNow());
        _queue.Add(item);
        _logger.LogTrace("Enqueued UDP message ({ByteCount} bytes). Queue depth: {QueueDepth}",
            data.Length, _queue.Count);
        return item;
    }

    /// <summary>
    /// Blocks until a message is available or cancellation is requested.
    /// Called exclusively by <see cref="Hosting.UdpConnectionManager"/>.
    /// </summary>
    internal UdpQueueItem Take(CancellationToken cancellationToken)
        => _queue.Take(cancellationToken);

    /// <summary>
    /// Re-adds a previously dequeued item back to the queue, preserving its attempt count.
    /// Called exclusively by <see cref="Hosting.UdpConnectionManager"/> for retry/requeue logic.
    /// </summary>
    internal void Requeue(UdpQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _queue.Add(item);
    }

    /// <summary>
    /// Signals that no more messages will be added to the queue.
    /// Called by <see cref="Hosting.UdpConnectionManager"/> during shutdown.
    /// </summary>
    internal void CompleteAdding() => _queue.CompleteAdding();

    public void Dispose()
    {
        if (_disposed)
            return;
        _queue.Dispose();
        _disposed = true;
    }
}
