namespace SRF.Network.Knx;

/// <summary>
/// Represents a connection to a KNX bus, managing the connection state, handling incoming messages, and providing an interface for sending messages.
/// Message queueing for outgoing messages, retry mechanisms for failed message sends, as well as queuing of
/// incoming messages for processing in a separate thread or task shall be implemented in this class to ensure
/// reliable communication with the KNX bus and to prevent blocking the main application thread during message handling.
/// 
/// <see cref="IKnxBus"/> shall be used for interacting with the underlying KNX bus and its connection protocols,
/// while this interface provides a higher-level abstraction for managing KNX connections and handling messages.
/// 
/// <see cref="IKnxConnection"/> was originally built to wrap Knx.Falcon.Sdk's KnxBus class into a DI context
/// with above mentioned features, but shall allow alternative implementations independent of Knx.Falcon.Sdk.
/// </summary>
public interface IKnxConnection
{
    event EventHandler<KnxConnectionEventArgs>? ConnectionStatusChanged;
    event EventHandler<KnxMessageReceivedEventArgs> MessageReceived;
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(IKnxMessage message, CancellationToken token);
    bool IsConnected { get; }
}
