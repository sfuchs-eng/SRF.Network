namespace SRF.Network.Knx;

/// <summary>
/// Processes the <see cref="IKnxMessage"/> instances that are received from the KNX bus.
/// </summary>
public interface IKnxMessageQueue
{
    public void Enqueue(KnxMessageContext message);

    /// <summary>
    /// Event that is raised when a new message is received from the KNX bus.
    /// </summary>
    public event KnxMessageHandlerDelegate MessageReceived;
}
