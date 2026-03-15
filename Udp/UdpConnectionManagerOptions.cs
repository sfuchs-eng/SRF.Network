namespace SRF.Network.Udp;

/// <summary>
/// Options for the UDP connection manager.
/// </summary>
public class UdpConnectionManagerOptions
{
    /// <summary>
    /// Default configuration section name in appsettings.json.
    /// </summary>
    public const string DefaultConfigSectionName = "Udp:ConnectionManager";

    /// <summary>
    /// Interval in seconds between connection attempts. Default is 10 seconds.
    /// </summary>
    public double ReconnectInterval { get; set; } = 10.0;

    /// <summary>
    /// Interval in seconds to wait before retrying a failed send. Default is 5 seconds.
    /// </summary>
    public double SendRetryInterval { get; set; } = 5.0;

    /// <summary>
    /// Maximum number of send attempts before giving up on a message. Default is 3.
    /// </summary>
    public int MaxSendAttempts { get; set; } = 3;

    /// <summary>
    /// Whether to automatically connect on startup. Default is true.
    /// </summary>
    public bool AutoConnect { get; set; } = true;
}
