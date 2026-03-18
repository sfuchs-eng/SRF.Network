using System.Net;

namespace SRF.Network.Udp;

/// <summary>
/// Event arguments for UDP messages received via multicast.
/// </summary>
public class UdpMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// The received message data.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// The remote endpoint that sent the message.
    /// </summary>
    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// The timestamp when the message was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; }

    public UdpMessageReceivedEventArgs(byte[] data, IPEndPoint remoteEndPoint, DateTimeOffset receivedAt)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
        ReceivedAt = receivedAt;
    }
}
