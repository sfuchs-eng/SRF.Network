using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx;

/// <summary>
/// This interface defines the contract for a KNX bus, including connection management, message handling, and event notifications.
/// Implementations must be thread-safe and support asynchronous operations for connecting and disconnecting from the KNX bus.
/// The <see cref="IKnxBus"/> interface is designed to be used in conjunction with the <see cref="IKnxConnection"/> interface,
/// which provides a higher-level abstraction for managing KNX connections and handling messages.
/// 
/// In a context where Knx.Falcon.Sdk is used, the <see cref="IKnxBus"/> implementation would typically wrap around the Falcon SDK's KnxBus class,
/// providing a consistent interface for the rest of the application to interact with the KNX bus.
/// </summary>
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