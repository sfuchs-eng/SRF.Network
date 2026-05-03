using System.Net;
using System.Net.NetworkInformation;
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
    private readonly TimeProvider _timeProvider;
    private UdpClient? _udpClient;
    private IPEndPoint? _multicastEndPoint;
    private IPAddress? _multicastAddress;
    private IPAddress? _localInterfaceAddress;
    private int? _localInterfaceIndex;
    private string? _localInterfaceDescription;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _isConnected;
    private readonly object _lock = new object();
    private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
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

    public UdpMulticastClient(IOptions<UdpMulticastOptions> options, ILogger<UdpMulticastClient> logger, TimeProvider timeProvider)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        if (IsConnected)
        {
            _logger.LogDebug("UDP multicast client is already connected (concurrent call, semaphore safety net).");
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

            // Resolve local interface (explicit LocalInterface > dynamic LocalIpAddress > default interface).
            var localCandidate = ResolveLocalInterfaceCandidate(_multicastAddress.AddressFamily);
            _localInterfaceAddress = localCandidate?.Address;
            _localInterfaceIndex = localCandidate?.InterfaceIndex;

            // Create UDP client
            _udpClient = new UdpClient(_multicastAddress.AddressFamily);
            if (_multicastAddress.AddressFamily == AddressFamily.InterNetworkV6)
                _udpClient.Client.DualMode = false;

            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _options.ReuseAddress);
            _udpClient.Client.ReceiveBufferSize = _options.ReceiveBufferSize;
            _udpClient.Client.SendBufferSize = _options.SendBufferSize;

            // Bind to the multicast port
            _udpClient.Client.Bind(new IPEndPoint(
                _multicastAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
                _options.Port));

            // Join the multicast group. If no local interface is configured,
            // use the platform default outbound interface instead of IPAddress.Any.
            if (_multicastAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (_localInterfaceIndex is int ifIndex && ifIndex > 0)
                    _udpClient.JoinMulticastGroup(ifIndex, _multicastAddress);
                else
                    _udpClient.JoinMulticastGroup(_multicastAddress);
            }
            else if (_localInterfaceAddress is null)
                _udpClient.JoinMulticastGroup(_multicastAddress);
            else
                _udpClient.JoinMulticastGroup(_multicastAddress, _localInterfaceAddress);
            _udpClient.MulticastLoopback = _options.MulticastLoopback;
            _udpClient.Ttl = (short)_options.TimeToLive;

            var effectiveInterface = ResolveEffectiveLocalInterface(_multicastAddress, localCandidate);
            _localInterfaceAddress = effectiveInterface?.PrimaryAddress ?? _localInterfaceAddress;
            _localInterfaceIndex = effectiveInterface?.InterfaceIndex ?? _localInterfaceIndex;
            _localInterfaceDescription = effectiveInterface?.Description
                ?? _localInterfaceAddress?.ToString()
                ?? "default";

            _logger.LogInformation("Successfully joined multicast group {MulticastAddress}:{Port} on interface {LocalInterface} (index={InterfaceIndex}, MulticastLoopback={MulticastLoopback})",
                _options.MulticastAddress,
                _options.Port,
                _localInterfaceDescription,
                _localInterfaceIndex?.ToString() ?? "default",
                _options.MulticastLoopback);

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
        finally
        {
            _connectLock.Release();
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
                var receivedAt = _timeProvider.GetUtcNow();

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
                    if (_multicastAddress.AddressFamily == AddressFamily.InterNetworkV6 && _localInterfaceIndex is int ifIndex && ifIndex > 0)
                        _udpClient.DropMulticastGroup(_multicastAddress, ifIndex);
                    else
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
        _localInterfaceDescription = null;
        _localInterfaceIndex = null;
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

    private LocalIpCandidate? ResolveLocalInterfaceCandidate(AddressFamily addressFamily)
    {
        if (!string.IsNullOrWhiteSpace(_options.LocalInterface))
        {
            if (!IPAddress.TryParse(_options.LocalInterface, out var explicitAddress)
                || explicitAddress.AddressFamily != addressFamily)
                throw new ArgumentException($"Invalid local interface address: {_options.LocalInterface}");

            var explicitMatch = GetLocalIpCandidates(addressFamily).FirstOrDefault(c => c.Address.Equals(explicitAddress));
            if (explicitMatch is not null)
                return explicitMatch;

            if (addressFamily == AddressFamily.InterNetworkV6)
                throw new ArgumentException($"LocalInterface address {_options.LocalInterface} is not assigned to an active network interface.");

            return new LocalIpCandidate(explicitAddress, null, NetworkInterfaceType.Unknown, string.Empty, null);
        }

        if (string.IsNullOrWhiteSpace(_options.LocalIpAddress))
            return null;

        var localCandidates = GetLocalIpCandidates(addressFamily).ToArray();
        var localAddresses = localCandidates.Select(c => c.Address).ToArray();

        if (TryParseCidr(_options.LocalIpAddress, addressFamily, out var cidrNetwork, out var cidrPrefix))
        {
            var cidrMatch = SelectPreferredCandidate(localCandidates
                .Where(c => GetNetworkAddress(c.Address, cidrPrefix).Equals(cidrNetwork)));
            if (cidrMatch is not null)
                return cidrMatch;

            _logger.LogWarning(
                "No active local interface address matched LocalIpAddress CIDR hint {LocalIpAddress}; falling back to default interface.",
                _options.LocalIpAddress);
            return null;
        }

        if (!IPAddress.TryParse(_options.LocalIpAddress, out var routeHint) || routeHint.AddressFamily != addressFamily)
            throw new ArgumentException($"Invalid local IP address hint: {_options.LocalIpAddress}");

        // If the hint equals a local unicast address, use it directly.
        var exactMatch = localCandidates.FirstOrDefault(c => c.Address.Equals(routeHint));
        if (exactMatch is not null)
            return exactMatch;

        // If the hint looks like a subnet base,
        // select an active local address that belongs to that subnet.
        var subnetMatch = SelectPreferredCandidate(localCandidates
            .Where(c => c.PrefixLength is int prefix
                        && GetNetworkAddress(c.Address, prefix).Equals(routeHint)));
        if (subnetMatch is not null)
            return subnetMatch;

        // Otherwise, derive the local egress address used to reach the hint.
        try
        {
            using var socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(routeHint, 9);
            if (socket.LocalEndPoint is IPEndPoint ep
                && ep.Address != IPAddress.Any
                && ep.Address != IPAddress.None
                && ep.Address != IPAddress.IPv6Any)
            {
                return localCandidates.FirstOrDefault(c => c.Address.Equals(ep.Address))
                    ?? new LocalIpCandidate(ep.Address, null, NetworkInterfaceType.Unknown, string.Empty, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to derive local interface from LocalIpAddress hint {LocalIpAddress}; falling back to default interface.",
                _options.LocalIpAddress);
        }

        _logger.LogWarning(
            "Could not derive a local interface from LocalIpAddress hint {LocalIpAddress}; falling back to default interface.",
            _options.LocalIpAddress);
        return null;
    }

    private static bool TryParseCidr(string value, AddressFamily expectedFamily, out IPAddress network, out int prefixLength)
    {
        network = expectedFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6None : IPAddress.None;
        prefixLength = 0;

        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var parsedNetwork)
            || parsedNetwork is null
            || parsedNetwork.AddressFamily != expectedFamily)
            return false;

        network = parsedNetwork;

        int maxPrefix = expectedFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix)
            return false;

        network = GetNetworkAddress(network, prefixLength);
        return true;
    }

    private static LocalIpCandidate? SelectPreferredCandidate(IEnumerable<LocalIpCandidate> candidates) =>
        candidates
            .OrderBy(c => GetInterfacePreference(c.InterfaceType))
            .ThenBy(c => c.InterfaceName, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int GetInterfacePreference(NetworkInterfaceType interfaceType)
    {
        // Prefer wired interfaces over wireless and other adapters.
        return interfaceType switch
        {
            NetworkInterfaceType.Ethernet => 1,
            NetworkInterfaceType.GigabitEthernet => 0,
            NetworkInterfaceType.FastEthernetFx => 3,
            NetworkInterfaceType.FastEthernetT => 3,
            NetworkInterfaceType.Wireless80211 => 2,
            _ => 4,
        };
    }

    private static IPAddress GetNetworkAddress(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var networkBytes = new byte[bytes.Length];

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        if (fullBytes > 0)
            Array.Copy(bytes, networkBytes, Math.Min(fullBytes, bytes.Length));

        if (fullBytes < bytes.Length && remainingBits > 0)
        {
            byte mask = (byte)(0xFF << (8 - remainingBits));
            networkBytes[fullBytes] = (byte)(bytes[fullBytes] & mask);
        }

        return new IPAddress(networkBytes);
    }

    private static int CountSetBits(IEnumerable<byte> bytes)
    {
        int count = 0;
        foreach (var b in bytes)
            count += System.Numerics.BitOperations.PopCount(b);
        return count;
    }

    private static IEnumerable<LocalIpCandidate> GetLocalIpCandidates(AddressFamily family) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                .Where(ua => ua.Address.AddressFamily == family)
                .Select(ua => new LocalIpCandidate(
                    Address: ua.Address,
                    PrefixLength: GetPrefixLength(ua),
                    InterfaceType: ni.NetworkInterfaceType,
                    InterfaceName: ni.Name ?? string.Empty,
                    InterfaceIndex: GetInterfaceIndex(ni, family))))
            .Where(c => !IPAddress.IsLoopback(c.Address));

    private static int? GetPrefixLength(UnicastIPAddressInformation address)
    {
        // Prefer framework-provided prefix length when available.
        if (address.PrefixLength is > 0 and <= 128)
            return address.PrefixLength;

        if (address.Address.AddressFamily == AddressFamily.InterNetwork && address.IPv4Mask is not null)
            return CountSetBits(address.IPv4Mask.GetAddressBytes());

        return null;
    }

    private static int? GetInterfaceIndex(NetworkInterface ni, AddressFamily family)
    {
        var props = ni.GetIPProperties();
        return family switch
        {
            AddressFamily.InterNetwork => props.GetIPv4Properties()?.Index,
            AddressFamily.InterNetworkV6 => props.GetIPv6Properties()?.Index,
            _ => null,
        };
    }

    private EffectiveLocalInterface? ResolveEffectiveLocalInterface(IPAddress multicastAddress, LocalIpCandidate? preferredCandidate)
    {
        if (preferredCandidate is not null)
            return DescribeLocalInterface(preferredCandidate);

        try
        {
            using var socket = new Socket(multicastAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(multicastAddress, _options.Port));

            if (socket.LocalEndPoint is not IPEndPoint localEndPoint)
                return null;

            if (localEndPoint.Address == IPAddress.Any
                || localEndPoint.Address == IPAddress.None
                || localEndPoint.Address == IPAddress.IPv6Any
                || localEndPoint.Address == IPAddress.IPv6None)
                return null;

            var localCandidate = GetLocalIpCandidates(multicastAddress.AddressFamily)
                .FirstOrDefault(candidate => candidate.Address.Equals(localEndPoint.Address));

            return localCandidate is not null
                ? DescribeLocalInterface(localCandidate)
                : new EffectiveLocalInterface(localEndPoint.Address, null, localEndPoint.Address.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to resolve the effective local multicast interface for {MulticastAddress}:{Port}; logging configured fallback.",
                multicastAddress,
                _options.Port);
            return null;
        }
    }

    private static EffectiveLocalInterface DescribeLocalInterface(LocalIpCandidate candidate)
    {
        var interfaceAddresses = GetLocalIpCandidates(candidate.Address.AddressFamily)
            .Where(current => current.InterfaceIndex == candidate.InterfaceIndex
                && string.Equals(current.InterfaceName, candidate.InterfaceName, StringComparison.Ordinal))
            .Select(current => current.Address.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(current => current, StringComparer.Ordinal)
            .ToArray();

        var description = interfaceAddresses.Length == 0 || string.IsNullOrWhiteSpace(candidate.InterfaceName)
            ? candidate.Address.ToString()
            : $"{candidate.InterfaceName} [{string.Join(", ", interfaceAddresses)}]";

        return new EffectiveLocalInterface(candidate.Address, candidate.InterfaceIndex, description);
    }

    private sealed record LocalIpCandidate(
        IPAddress Address,
        int? PrefixLength,
        NetworkInterfaceType InterfaceType,
        string InterfaceName,
        int? InterfaceIndex);

    private sealed record EffectiveLocalInterface(
        IPAddress PrimaryAddress,
        int? InterfaceIndex,
        string Description);

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
            _connectLock.Dispose();
        }

        _disposed = true;
    }
}
