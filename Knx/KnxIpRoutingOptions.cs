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
    /// Maximum number of consecutive outbound telegrams that can be sent in a burst before
    /// the bus load is re-evaluated. Acts as the token bucket capacity.
    /// <para>
    /// When the bus is idle, tokens accumulate at one per <see cref="MinTelegramInterval"/> up to
    /// this cap. Received telegrams contribute to the load window but do not consume tokens.
    /// </para>
    /// Default: <c>5</c>. Values ≤ 0 are treated as <c>1</c> (no burst, but rate limiting remains active).
    /// </summary>
    public int MaxBurstSize { get; set; } = 5;

    /// <summary>
    /// Bus load fraction (0–1) above which the rate limiter enters high-load mode and only
    /// allows burst-token spending (no new token generation through waiting).
    /// <para>
    /// Load is measured over a sliding window of duration <see cref="LoadWindowDuration"/> as the
    /// ratio of observed bus events (sends + receives) to the theoretical maximum.
    /// </para>
    /// Default: <c>0.5</c> (50 %). Set to <c>1.0</c> to disable high-load mode entirely.
    /// </summary>
    public double MaxContinuousBusLoad { get; set; } = 0.5;

    /// <summary>
    /// How long the cooldown period lasts after the burst token pool is exhausted under high load.
    /// During cooldown, sends are rate-limited to <see cref="CooldownMaxBusLoad"/> of the bus
    /// capacity to allow the bus to recover.
    /// Default: <c>1 second</c>.
    /// </summary>
    public TimeSpan CooldownDuration { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum bus load fraction (0–1) allowed during a cooldown period.
    /// Outbound sends are spaced at <c>MinTelegramInterval / CooldownMaxBusLoad</c> apart.
    /// <para>
    /// Set to <c>0</c> to completely pause sending during cooldown (until the cooldown expires).
    /// </para>
    /// Default: <c>0.2</c> (20 %).
    /// </summary>
    public double CooldownMaxBusLoad { get; set; } = 0.2;

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

    /// <summary>
    /// Duration of the sliding window used to compute the current bus load.
    /// Computed as <c>max(MaxBurstSize, 1) × MinTelegramInterval</c>.
    /// Returns <see cref="TimeSpan.Zero"/> when rate limiting is disabled.
    /// </summary>
    public TimeSpan LoadWindowDuration =>
        MinTelegramInterval == TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Max(MaxBurstSize, 1) * MinTelegramInterval.Ticks);
}
