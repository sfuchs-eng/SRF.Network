namespace SRF.Network.Knx;

public interface IKnxConnection
{
    event EventHandler<KnxConnectionEventArgs> ConnectionStatusChanged;
    event EventHandler<KnxMessageReceivedEventArgs> MessageReceived;
    void Connect();
    void Disconnect();
    void SendMessage(IKnxMessage message);
    bool IsConnected { get; }
}
