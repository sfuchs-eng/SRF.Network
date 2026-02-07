using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx;

public class KnxMessageReceivedEventArgs
{
    public KnxMessageContext KnxMessageContext { get; init; }

    public KnxMessageReceivedEventArgs(KnxMessageContext context)
    {
        KnxMessageContext = context;
    }

    public KnxMessageReceivedEventArgs(GroupEventArgs groupEventArgs)
    {
        KnxMessageContext = new(groupEventArgs);
    }
}