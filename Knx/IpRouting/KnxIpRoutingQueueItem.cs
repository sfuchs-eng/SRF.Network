namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// Represents a KNX/IP routing frame queued for rate-limited transmission.
/// </summary>
internal sealed class KnxIpRoutingQueueItem
{
    /// <summary>The encoded KNX/IP UDP payload to transmit.</summary>
    internal byte[] Data { get; }

    /// <summary>KNX TP wire bit count of this telegram, used for rate-limiting.</summary>
    internal int Bits { get; }

    internal KnxIpRoutingQueueItem(byte[] data, int bits)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Bits = bits;
    }
}
