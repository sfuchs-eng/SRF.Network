namespace SRF.Network.Knx;

public interface IKnxMessageHandler
{
    Task HandleMessageAsync(IKnxConnection receiver, KnxMessageContext message);
}

public delegate Task KnxMessageHandlerDelegate(IKnxConnection receiver, KnxMessageContext message);
