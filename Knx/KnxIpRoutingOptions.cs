namespace SRF.Network.Knx;

/// <summary>
/// Configuration options for <see cref="Connection.KnxIpRoutingBus"/>, including bus rate limiting.
/// </summary>
public class KnxIpRoutingOptions
{
    /// <summary>The default configuration section name.</summary>
    public const string DefaultConfigSectionName = "Knx:IpRouting";

    /// <summary>
    /// KNX TP physical layer bit rate in bits per second.
    /// The standard KNX TP bit rate is 9600 bit/s.
    /// Default: <c>9600</c>.
    /// </summary>
    public int BusBitRate { get; set; } = 9600;

    /// <summary>
    /// Average number of bits per telegram, including physical layer overhead
    /// (preamble, stop bits, ACK, inter-frame gap).
    /// A typical KNX group telegram with a small payload requires approximately 134–200 bits.
    /// Default: <c>200</c>, yielding approximately 48 telegrams/s capacity on a single line.
    /// Set to <c>0</c> to disable rate limiting.
    /// </summary>
    public int AverageTelegramBits { get; set; } = 200;

    /// <summary>
    /// Number of independent KNX TP lines. Increases the effective throughput proportionally.
    /// Default: <c>1</c>.
    /// </summary>
    public int BusLineCount { get; set; } = 1;

    /// <summary>
    /// Minimum interval between consecutive bus events (send or receive), computed from the
    /// physical bus parameters.
    /// <para>
    /// Formula: <c>AverageTelegramBits / (BusBitRate × BusLineCount)</c>
    /// </para>
    /// <para>
    /// With defaults (9600 bit/s, 200 bits/telegram, 1 line): ≈ 20.83 ms.
    /// Returns <see cref="TimeSpan.Zero"/> when any input is ≤ 0 (rate limiting disabled).
    /// </para>
    /// </summary>
    public TimeSpan MinTelegramInterval =>
        AverageTelegramBits <= 0 || BusBitRate <= 0 || BusLineCount <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(AverageTelegramBits / (double)(BusBitRate * BusLineCount));
}
