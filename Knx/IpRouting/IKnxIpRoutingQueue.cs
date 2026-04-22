namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// A rate-limited send queue for KNX/IP routing frames.
/// </summary>
public interface IKnxIpRoutingQueue
{
    /// <summary>
    /// Enqueues an encoded KNX/IP frame for rate-limited transmission.
    /// Non-blocking; the actual UDP send and rate-limiting delay happen in the background sender.
    /// </summary>
    /// <param name="data">The encoded KNX/IP UDP payload.</param>
    /// <param name="bits">KNX TP wire bit count of the telegram, used for rate-limiting.</param>
    void Enqueue(byte[] data, int bits);

    /// <summary>
    /// Records an incoming telegram in the bus load window for rate-limit calculations.
    /// Non-blocking; does not consume bit credit or block pending sends.
    /// </summary>
    /// <param name="bits">KNX TP wire bit count of the received telegram.</param>
    void NotifyReceived(int bits);
}
