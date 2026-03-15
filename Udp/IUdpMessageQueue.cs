namespace SRF.Network.Udp;

/// <summary>
/// Injectable service for enqueueing UDP messages for transmission.
/// Consumers inject this interface to send UDP messages without needing to
/// know about connection state or retry logic.
/// Register via <c>AddUdpMulticastWithConnectionManager()</c> which also
/// registers the background service that drains this queue.
/// </summary>
public interface IUdpMessageQueue
{
    /// <summary>
    /// Enqueues a message for transmission. The message will be sent
    /// when a connection is available and retried on failure.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <returns>A queue item that can be used to track send status via events.</returns>
    UdpQueueItem Enqueue(byte[] data);

    /// <summary>
    /// Current number of messages pending in the queue.
    /// </summary>
    int QueuedMessageCount { get; }

    /// <summary>
    /// Indicates whether the queue has been completed (no more items can be added).
    /// </summary>
    bool IsCompleted { get; }
}
