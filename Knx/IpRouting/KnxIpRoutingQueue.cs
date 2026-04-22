using System.Collections.Concurrent;
using SRF.Network.Knx.Connection;

namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// Singleton send queue for KNX/IP routing frames. Holds the encoded frames and delegates
/// rate-limiting to an internal <see cref="KnxBusRateLimiter"/>. The actual UDP send and
/// rate-limiting delay are performed by the <c>KnxIpRoutingSender</c> background service
/// which injects this class directly to access its <c>internal</c> members.
/// <para>
/// Consumers should inject <see cref="IKnxIpRoutingQueue"/> to enqueue messages.
/// </para>
/// </summary>
public sealed class KnxIpRoutingQueue : IKnxIpRoutingQueue, IDisposable
{
    private readonly BlockingCollection<KnxIpRoutingQueueItem> _queue = new();
    private readonly KnxBusRateLimiter _rateLimiter;

    /// <summary>
    /// Initializes a new instance of <see cref="KnxIpRoutingQueue"/>.
    /// </summary>
    public KnxIpRoutingQueue(KnxIpRoutingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _rateLimiter = new KnxBusRateLimiter(options, timeProvider);
    }

    /// <inheritdoc/>
    public void Enqueue(byte[] data, int bits)
    {
        ArgumentNullException.ThrowIfNull(data);
        _queue.Add(new KnxIpRoutingQueueItem(data, bits));
    }

    /// <inheritdoc/>
    public void NotifyReceived(int bits) => _rateLimiter.NotifyReceived(bits);

    /// <summary>
    /// Blocks the caller until a queued item is available, then returns it.
    /// Used internally by <c>KnxIpRoutingSender</c>.
    /// </summary>
    internal KnxIpRoutingQueueItem Take(CancellationToken cancellationToken) =>
        _queue.Take(cancellationToken);

    /// <summary>
    /// Waits until the rate limiter permits sending a telegram of <paramref name="bits"/> bits.
    /// Used internally by <c>KnxIpRoutingSender</c> before each UDP send.
    /// </summary>
    internal Task WaitForSendSlotAsync(int bits, CancellationToken cancellationToken) =>
        _rateLimiter.WaitForSendSlotAsync(bits, cancellationToken);

    /// <summary>
    /// Signals that no more items will be added to the queue.
    /// Used internally by <c>KnxIpRoutingSender</c> during shutdown.
    /// </summary>
    internal void CompleteAdding() => _queue.CompleteAdding();

    /// <summary>Gets a value indicating whether the queue has been completed and is empty.</summary>
    internal bool IsCompleted => _queue.IsCompleted;

    /// <inheritdoc/>
    public void Dispose() => _queue.Dispose();
}
