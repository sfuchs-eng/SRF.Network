namespace SRF.Network.Udp;

/// <summary>
/// Configuration options for UDP multicast communication.
/// </summary>
public class UdpMulticastOptions
{
    /// <summary>
    /// Base configuration section name in appsettings.json.
    /// Named connections live under <c>Udp:Connections:{name}</c>.
    /// </summary>
    public const string DefaultConfigSectionName = "Udp:Connections";

    /// <summary>
    /// The multicast group address to join (e.g., "224.0.23.12" for KNX).
    /// </summary>
    public string MulticastAddress { get; set; } = "224.0.0.1";

    /// <summary>
    /// The UDP port to use for multicast communication.
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// Optional local interface IP address to use for multicast group membership.
    /// If null, the OS default multicast interface is used.
    /// </summary>
    public string? LocalInterface { get; set; }

    /// <summary>
    /// Optional IP address hint used to dynamically select the outbound local interface.
    /// When set, the client derives a suitable local interface address from this hint.
    /// The value may be an interface host IP, a subnet base address, or CIDR notation
    /// for both IPv4 and IPv6 families (matching the configured multicast address family).
    /// Ignored when <see cref="LocalInterface"/> is set.
    /// </summary>
    public string? LocalIpAddress { get; set; }

    /// <summary>
    /// The Time-To-Live value for multicast packets (1-255). Default is 16.
    /// </summary>
    public int TimeToLive { get; set; } = 16;

    /// <summary>
    /// Size of the receive buffer in bytes. Default is 8192.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// Size of the send buffer in bytes. Default is 8192.
    /// </summary>
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// Whether to allow multiple sockets to bind to the same address/port. Default is true.
    /// </summary>
    public bool ReuseAddress { get; set; } = true;

    /// <summary>
    /// Whether sent multicast datagrams are looped back to this host. Default is false.
    /// Set to true only when local echo is explicitly desired.
    /// </summary>
    public bool MulticastLoopback { get; set; } = false;

    /// <summary>
    /// Timeout for receive operations. Default is 5 seconds.
    /// </summary>
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
