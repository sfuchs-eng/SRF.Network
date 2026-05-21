namespace SRF.Network.Knx;

/// <summary>
/// Administrator-facing KNX/IP connection options.
/// </summary>
/// <remarks>
/// <para>
/// These options are bound per named connection under <c>Knx:Connections:{name}</c>
/// and projected into retained UDP transport options at service registration time.
/// </para>
/// <para>
/// Structured properties are primary. <see cref="ConnectionString"/> can be used as
/// optional fallback input for values that were not set structurally.
/// </para>
/// </remarks>
public sealed class KnxConnectionOptions
{
    /// <summary>
    /// Named connection section root.
    /// </summary>
    public const string DefaultConfigSectionName = "Knx:Connections";

    /// <summary>
    /// Optional Falcon-style key/value connection string.
    /// </summary>
    public string? ConnectionString { get; set; } = "Type=IpRouting";

    /// <summary>
    /// Optional local KNX individual address used in outbound cEMI frames.
    /// </summary>
    public string? KnxAddress { get; set; }

    /// <summary>
    /// Optional multicast address for KNX/IP routing.
    /// </summary>
    public string? MulticastAddress { get; set; }

    /// <summary>
    /// Optional UDP port for KNX/IP routing.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Optional local interface address used for multicast membership.
    /// </summary>
    public string? LocalInterface { get; set; }

    /// <summary>
    /// Optional local IP/subnet hint for outbound multicast interface selection.
    /// </summary>
    public string? LocalIpAddress { get; set; }

    /// <summary>
    /// Optional multicast TTL.
    /// </summary>
    public int? TimeToLive { get; set; }

    /// <summary>
    /// Optional receive buffer size.
    /// </summary>
    public int? ReceiveBufferSize { get; set; }

    /// <summary>
    /// Optional send buffer size.
    /// </summary>
    public int? SendBufferSize { get; set; }

    /// <summary>
    /// Optional reuse-address flag.
    /// </summary>
    public bool? ReuseAddress { get; set; }

    /// <summary>
    /// Optional multicast loopback flag.
    /// </summary>
    public bool? MulticastLoopback { get; set; }

    /// <summary>
    /// Optional receive timeout.
    /// </summary>
    public TimeSpan? ReceiveTimeout { get; set; }

    /// <summary>
    /// Optional UDP connection manager reconnect interval in seconds.
    /// </summary>
    public double? ReconnectInterval { get; set; }

    /// <summary>
    /// Optional UDP connection manager send retry interval in seconds.
    /// </summary>
    public double? SendRetryInterval { get; set; }

    /// <summary>
    /// Optional UDP connection manager max send attempts.
    /// </summary>
    public int? MaxSendAttempts { get; set; }

    /// <summary>
    /// Optional UDP connection manager auto-connect flag.
    /// </summary>
    public bool? AutoConnect { get; set; }

    /// <summary>
    /// Returns an effective immutable view by applying optional connection-string
    /// fallback values for unset structured properties.
    /// </summary>
    public EffectiveKnxConnectionOptions ToEffective()
    {
        var knxAddress = FirstNonEmpty(KnxAddress, GetToken("KnxAddress"), "0.0.1") ?? "0.0.1";
        var multicastAddress = FirstNonEmpty(MulticastAddress, GetToken("MulticastAddress"), "224.0.23.12") ?? "224.0.23.12";

        return new EffectiveKnxConnectionOptions(
            KnxAddress: knxAddress,
            MulticastAddress: multicastAddress,
            Port: Port ?? ParseInt(GetToken("Port")) ?? 3671,
            LocalInterface: FirstNonEmpty(LocalInterface, GetToken("LocalInterface"), null),
            LocalIpAddress: FirstNonEmpty(LocalIpAddress, GetToken("LocalIpAddress"), null),
            TimeToLive: TimeToLive,
            ReceiveBufferSize: ReceiveBufferSize,
            SendBufferSize: SendBufferSize,
            ReuseAddress: ReuseAddress,
            MulticastLoopback: MulticastLoopback ?? ParseBool(GetToken("MulticastLoopback")),
            ReceiveTimeout: ReceiveTimeout,
            ReconnectInterval: ReconnectInterval,
            SendRetryInterval: SendRetryInterval,
            MaxSendAttempts: MaxSendAttempts,
            AutoConnect: AutoConnect);
    }

    private string? GetToken(string key)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return null;

        foreach (var token in ConnectionString.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = token.IndexOf('=');
            if (idx < 0)
                continue;

            var k = token[..idx].Trim();
            var v = token[(idx + 1)..].Trim();
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    private static string? FirstNonEmpty(string? first, string? second, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(first))
            return first;
        if (!string.IsNullOrWhiteSpace(second))
            return second;
        return fallback;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static bool? ParseBool(string? value)
        => bool.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>
/// Effective KNX/IP connection options after fallback normalization.
/// </summary>
public sealed record EffectiveKnxConnectionOptions(
    string KnxAddress,
    string MulticastAddress,
    int Port,
    string? LocalInterface,
    string? LocalIpAddress,
    int? TimeToLive,
    int? ReceiveBufferSize,
    int? SendBufferSize,
    bool? ReuseAddress,
    bool? MulticastLoopback,
    TimeSpan? ReceiveTimeout,
    double? ReconnectInterval,
    double? SendRetryInterval,
    int? MaxSendAttempts,
    bool? AutoConnect);