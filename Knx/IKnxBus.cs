using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx;

public interface IKnxBus
{
    bool IsConnected { get; }

    event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;

    event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

    BusConnectionState ConnectionState { get; }

    event EventHandler<GroupEventArgs> GroupMessageReceived;

    Task ConnectAsync();

    Task DisconnectAsync();
}