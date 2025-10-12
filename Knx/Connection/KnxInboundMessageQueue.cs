using System.Threading.Channels;

namespace SRF.Network.Knx.Connection;

public class KnxInboundMessageQueue : IKnxMessageQueue
{
    /// <summary>
    /// Event raised and executed by the worker thread processing the messages in the queue received from the KNX bus.
    /// </summary>
    public event KnxMessageHandlerDelegate? MessageReceived;

    /// <summary>
    /// Enqueues a message received from the KNX bus for processing.
    /// </summary>
    /// <param name="message"></param>
    public void Enqueue(KnxMessageContext message)
    {
        throw new NotImplementedException();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="queueProcessor">should implement a SetQueueReader method.</param>
    /// <param name="channelReaderSetter">remove, replace by queueProcessor</param>
    public void SetProcessor(IQueueProcessor queueProcessor, Action<ChannelReader<KnxMessageContext>> channelReaderSetter)
    {
        channelReaderSetter(...);
    }
}
