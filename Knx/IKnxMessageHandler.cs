namespace SRF.Network.Knx;

[Obsolete]
public interface IKnxMessageHandler
{
    Task HandleMessageAsync(IKnxConnection receiver, KnxMessageContext message);
}

[Obsolete]
public delegate Task KnxMessageHandlerDelegate(IKnxConnection receiver, KnxMessageContext message);
