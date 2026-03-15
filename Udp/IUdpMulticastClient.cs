namespace SRF.Network.Udp;

/// <summary>
/// Interface for a UDP multicast client that supports sending and receiving messages.
/// </summary>
public interface IUdpMulticastClient : IDisposable
{
    /// <summary>
    /// Indicates whether the client is currently connected to the multicast group.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event raised when the connection status changes.
    /// </summary>
    event EventHandler<UdpConnectionEventArgs>? ConnectionStatusChanged;

    /// <summary>
    /// Event raised when a message is received from the multicast group.
    /// </summary>
    event EventHandler<UdpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Connects to the multicast group and starts listening for messages.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the multicast group and stops listening for messages.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to the multicast group.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SendAsync(byte[] data, CancellationToken cancellationToken = default);
}
