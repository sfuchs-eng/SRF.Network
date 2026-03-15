using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SRF.Network.Udp;

/// <summary>
/// Implementation of a UDP multicast client for sending and receiving messages.
/// </summary>
public class UdpMulticastClient : IUdpMulticastClient
{
    private readonly UdpMulticastOptions _options;
    private readonly ILogger<UdpMulticastClient> _logger;
    private UdpClient? _udpClient;
    private IPEndPoint? _multicastEndPoint;
    private IPAddress? _multicastAddress;
    private IPAddress? _localInterfaceAddress;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _isConnected;
    private readonly object _lock = new object();
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _isConnected;
            }
        }
        private set
        {
            lock (_lock)
            {
                _isConnected = value;
            }
        }
    }

    public event EventHandler<UdpConnectionEventArgs>? ConnectionStatusChanged;
    public event EventHandler<UdpMessageReceivedEventArgs>? MessageReceived;

    public UdpMulticastClient(IOptions<UdpMulticastOptions> options, ILogger<UdpMulticastClient> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogWarning("UDP multicast client is already connected.");
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to UDP multicast group {MulticastAddress}:{Port}",
                _options.MulticastAddress, _options.Port);

            // Parse multicast address
            if (!IPAddress.TryParse(_options.MulticastAddress, out _multicastAddress))
            {
                throw new ArgumentException($"Invalid multicast address: {_options.MulticastAddress}");
            }

            _multicastEndPoint = new IPEndPoint(_multicastAddress, _options.Port);

            // Parse local interface if specified
            if (!string.IsNullOrEmpty(_options.LocalInterface))
            {
                if (!IPAddress.TryParse(_options.LocalInterface, out _localInterfaceAddress))
                {
                    throw new ArgumentException($"Invalid local interface address: {_options.LocalInterface}");
                }
            }
            else
            {
                _localInterfaceAddress = IPAddress.Any;
            }

            // Create UDP client
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _options.ReuseAddress);
            _udpClient.Client.ReceiveBufferSize = _options.ReceiveBufferSize;
            _udpClient.Client.SendBufferSize = _options.SendBufferSize;

            // Bind to the multicast port
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _options.Port));

            // Join the multicast group
            _udpClient.JoinMulticastGroup(_multicastAddress, _localInterfaceAddress);
            _udpClient.MulticastLoopback = true;
            _udpClient.Ttl = (short)_options.TimeToLive;

            _logger.LogInformation("Successfully joined multicast group {MulticastAddress}:{Port} on interface {LocalInterface}",
                _options.MulticastAddress, _options.Port, _localInterfaceAddress);

            // Start receive loop
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);

            IsConnected = true;
            OnConnectionStatusChanged(new UdpConnectionEventArgs(true));

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to UDP multicast group {MulticastAddress}:{Port}",
                _options.MulticastAddress, _options.Port);
            
            await CleanupAsync();
            OnConnectionStatusChanged(new UdpConnectionEventArgs(false, ex.Message));
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("UDP multicast client is not connected.");
            return;
        }

        _logger.LogInformation("Disconnecting from UDP multicast group {MulticastAddress}:{Port}",
            _options.MulticastAddress, _options.Port);

        await CleanupAsync();
        IsConnected = false;
        OnConnectionStatusChanged(new UdpConnectionEventArgs(false));

        _logger.LogInformation("Disconnected from UDP multicast group.");
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty.", nameof(data));
        }

        if (!IsConnected)
        {
            throw new InvalidOperationException("Cannot send data. Client is not connected.");
        }

        try
        {
            _logger.LogTrace("Sending {ByteCount} bytes to multicast group {MulticastAddress}:{Port}",
                data.Length, _options.MulticastAddress, _options.Port);

            var bytesSent = await _udpClient!.SendAsync(data, data.Length, _multicastEndPoint);

            _logger.LogTrace("Sent {BytesSent} bytes to multicast group.", bytesSent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send data to multicast group {MulticastAddress}:{Port}",
                _options.MulticastAddress, _options.Port);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting UDP receive loop.");

        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(cancellationToken);
                var receivedAt = DateTime.UtcNow;

                _logger.LogTrace("Received {ByteCount} bytes from {RemoteEndPoint}",
                    result.Buffer.Length, result.RemoteEndPoint);

                OnMessageReceived(new UdpMessageReceivedEventArgs(result.Buffer, result.RemoteEndPoint, receivedAt));
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Receive loop cancelled.");
                break;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("UDP client disposed during receive.");
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket exception in receive loop: {Message}", ex.Message);
                
                // Consider this a connection failure
                IsConnected = false;
                OnConnectionStatusChanged(new UdpConnectionEventArgs(false, ex.Message));
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in receive loop: {Message}", ex.Message);
                
                // Give it a moment before continuing to avoid tight error loops
                await Task.Delay(100, cancellationToken);
            }
        }

        _logger.LogDebug("UDP receive loop stopped.");
    }

    private async Task CleanupAsync()
    {
        // Cancel receive loop
        if (_receiveCts != null && !_receiveCts.IsCancellationRequested)
        {
            _receiveCts.Cancel();
        }

        // Wait for receive task to complete
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while waiting for receive task to complete.");
            }
        }

        // Leave multicast group and dispose client
        if (_udpClient != null)
        {
            try
            {
                if (_multicastAddress != null)
                {
                    _udpClient.DropMulticastGroup(_multicastAddress);
                    _logger.LogDebug("Left multicast group {MulticastAddress}", _multicastAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while leaving multicast group.");
            }

            _udpClient.Dispose();
            _udpClient = null;
        }

        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveTask = null;
    }

    protected virtual void OnConnectionStatusChanged(UdpConnectionEventArgs e)
    {
        _logger.LogTrace("Connection status changed: IsConnected={IsConnected}, Error={ErrorMessage}",
            e.IsConnected, e.ErrorMessage);
        ConnectionStatusChanged?.Invoke(this, e);
    }

    protected virtual void OnMessageReceived(UdpMessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (IsConnected)
            {
                // Fire and forget - we're disposing
                _ = DisconnectAsync();
            }
        }

        _disposed = true;
    }
}
