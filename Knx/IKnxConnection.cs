using System.Runtime.CompilerServices;

namespace SRF.Network.Knx;

public interface IKnxConnection
{
    event EventHandler<KnxConnectionEventArgs>? ConnectionStatusChanged;
    event EventHandler<KnxMessageReceivedEventArgs> MessageReceived;
    Task ConnectAsync();
    Task DisconnectAsync();
    Task SendMessageAsync(IKnxMessage message, CancellationToken token);
    bool IsConnected { get; }
}
