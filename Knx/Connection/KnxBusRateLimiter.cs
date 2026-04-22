namespace SRF.Network.Knx.Connection;

/// <summary>
/// Bits-based token-bucket rate limiter for KNX bus access with sliding-window load awareness
/// and burst-with-cooldown behavior.
/// <para>
/// Each telegram is weighted by its actual KNX TP wire bit count
/// (cEMI bytes × <see cref="KnxIpRoutingOptions.BitsPerByte"/> + <see cref="KnxIpRoutingOptions.PhysicalOverheadBitsPerTelegram"/>),
/// giving an accurate representation of physical bus utilization.
/// </para>
/// <para>
/// <b>Normal mode</b> (bus load ≤ <see cref="KnxIpRoutingOptions.MaxContinuousBusLoad"/>):
/// bit-credit accumulates at <c>BusBitRate × BusLineCount</c> bits/second, capped at
/// <see cref="KnxIpRoutingOptions.MaxBurstBits"/>. Outbound sends wait until enough credit is available.
/// </para>
/// <para>
/// <b>High-load mode</b> (bus load &gt; threshold): only existing burst credit may be spent.
/// When the burst pool is exhausted the limiter transitions to cooldown.
/// </para>
/// <para>
/// <b>Cooldown mode</b>: sends are spaced so that the outbound bit rate does not exceed
/// <c>BusBitRate × BusLineCount × CooldownMaxBusLoad</c> for <see cref="KnxIpRoutingOptions.CooldownDuration"/>,
/// after which normal mode resumes.
/// </para>
/// <para>
/// Received telegrams (via <see cref="NotifyReceived"/>) are recorded in the load window only.
/// They do not consume bit credit or block pending sends, but they increase the measured bus load
/// and can trigger high-load or cooldown transitions.
/// </para>
/// </summary>
internal sealed class KnxBusRateLimiter
{
    private readonly double _busBitsPerSecond;  // BusBitRate * BusLineCount
    private readonly double _maxBurstBits;
    private readonly double _maxContinuousLoad;
    private readonly TimeSpan _cooldownDuration;
    private readonly double _cooldownMaxLoad;
    private readonly TimeSpan _loadWindowDuration;

    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    // Bus event window (sends + receives, weighted by bits) — guarded by _lock
    private readonly Queue<(long ticks, int bits)> _busEvents = new();
    private readonly object _lock = new();

    // Token bucket — only accessed while holding _sendGate (single-writer), no extra lock needed
    private double _availableBits;        // 0 .. _maxBurstBits
    private long _lastRefillTicks;        // when credit was last topped up (0 = never)
    private long _lastSendTicks;          // when the last send was recorded (0 = never)
    private long _cooldownStartTicks;     // 0 = not in cooldown

    internal KnxBusRateLimiter(KnxIpRoutingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _busBitsPerSecond  = (double)options.BusBitRate * Math.Max(options.BusLineCount, 1);
        _maxBurstBits      = Math.Max(options.MaxBurstBits, 1);
        _maxContinuousLoad = options.MaxContinuousBusLoad;
        _cooldownDuration  = options.CooldownDuration;
        _cooldownMaxLoad   = options.CooldownMaxBusLoad;
        _loadWindowDuration = options.LoadWindowDuration;

        _timeProvider  = timeProvider;
        _availableBits = _maxBurstBits; // full bucket at startup
    }

    /// <summary>
    /// Records an incoming telegram in the bus load window.
    /// Non-blocking and thread-safe; does not consume bit credit or block pending sends.
    /// </summary>
    /// <param name="bits">KNX TP wire bit count of the received telegram.</param>
    internal void NotifyReceived(int bits)
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        lock (_lock)
            _busEvents.Enqueue((nowTicks, bits));
    }

    /// <summary>
    /// Waits until the bus rate allows the next outbound send, then claims the send slot.
    /// Concurrent callers are serialized through the send gate; each evaluates the current
    /// load independently after the previous sender has finished.
    /// </summary>
    /// <param name="bits">KNX TP wire bit count of the telegram about to be sent.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    internal async Task WaitForSendSlotAsync(int bits, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_busBitsPerSecond <= 0.0)
            {
                // Rate limiting disabled — record send and return immediately.
                RecordSendEvent(_timeProvider.GetUtcNow().UtcTicks, bits);
                return;
            }

            var delay = ComputeDelay(bits);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                // Refill credit for the time we just waited.
                RefillCredit(_timeProvider.GetUtcNow().UtcTicks);
            }

            // Consume bit credit and record the send.
            _availableBits = Math.Max(0.0, _availableBits - bits);
            RecordSendEvent(_timeProvider.GetUtcNow().UtcTicks, bits);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    // ---- Private helpers ----

    /// <summary>
    /// Computes the delay the caller must wait before sending <paramref name="bits"/> bits.
    /// May transition state to cooldown as a side effect.
    /// Must be called while holding the send gate.
    /// </summary>
    private TimeSpan ComputeDelay(int bits)
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        RefillCredit(nowTicks);

        var load = ComputeLoad(nowTicks);

        // --- Cooldown active ---
        if (IsCooldownActive(nowTicks))
            return CooldownSendDelay(nowTicks, bits);

        // --- Normal mode: load at or below threshold ---
        if (load <= _maxContinuousLoad)
        {
            if (_availableBits >= bits)
                return TimeSpan.Zero;

            // Wait until enough credit has accumulated for this telegram.
            var deficit = bits - _availableBits;
            return TimeSpan.FromSeconds(deficit / _busBitsPerSecond);
        }

        // --- High-load mode: load above threshold ---
        if (_availableBits >= bits)
            return TimeSpan.Zero; // spend burst credit; no delay

        // Burst pool exhausted under high load → enter cooldown.
        _cooldownStartTicks = nowTicks;
        return CooldownSendDelay(nowTicks, bits);
    }

    /// <summary>
    /// Returns the delay required in cooldown mode so that outbound sends do not exceed
    /// <see cref="_cooldownMaxLoad"/> of bus capacity.
    /// </summary>
    private TimeSpan CooldownSendDelay(long nowTicks, int bits)
    {
        if (_cooldownMaxLoad <= 0.0)
        {
            // Completely paused — wait until cooldown expires, then resume with zero delay.
            var cooldownRemaining = _cooldownStartTicks + _cooldownDuration.Ticks - nowTicks;
            return cooldownRemaining > 0 ? TimeSpan.FromTicks(cooldownRemaining) : TimeSpan.Zero;
        }

        // Effective bit rate during cooldown = _busBitsPerSecond * _cooldownMaxLoad.
        // Interval for this telegram = bits / effectiveBitRate.
        var cooldownIntervalSeconds = bits / (_busBitsPerSecond * _cooldownMaxLoad);

        if (_lastSendTicks == 0L)
            return TimeSpan.Zero;

        var elapsedSeconds = (nowTicks - _lastSendTicks) / (double)TimeSpan.TicksPerSecond;
        var remaining = cooldownIntervalSeconds - elapsedSeconds;
        return remaining > 0.0 ? TimeSpan.FromSeconds(remaining) : TimeSpan.Zero;
    }

    /// <summary>
    /// Adds bit credit for the time elapsed since the last refill. Capped at <see cref="_maxBurstBits"/>.
    /// Must be called while holding the send gate (single-writer).
    /// </summary>
    private void RefillCredit(long nowTicks)
    {
        if (_lastRefillTicks == 0L)
        {
            _lastRefillTicks = nowTicks;
            return;
        }

        var elapsedSeconds = (nowTicks - _lastRefillTicks) / (double)TimeSpan.TicksPerSecond;
        if (elapsedSeconds <= 0.0)
            return;

        var newBits = elapsedSeconds * _busBitsPerSecond;
        _availableBits = Math.Min(_availableBits + newBits, _maxBurstBits);
        _lastRefillTicks = nowTicks;
    }

    /// <summary>
    /// Returns the bus load (0–1) measured over the <see cref="_loadWindowDuration"/> sliding window.
    /// Prunes events older than the window as a side effect.
    /// The load is the ratio of total bits observed in the window to <see cref="_maxBurstBits"/>.
    /// </summary>
    private double ComputeLoad(long nowTicks)
    {
        if (_loadWindowDuration == TimeSpan.Zero)
            return 0.0;

        var cutoff = nowTicks - _loadWindowDuration.Ticks;
        lock (_lock)
        {
            while (_busEvents.Count > 0 && _busEvents.Peek().ticks < cutoff)
                _busEvents.Dequeue();

            var bitsInWindow = 0L;
            foreach (var (_, bits) in _busEvents)
                bitsInWindow += bits;

            return bitsInWindow / _maxBurstBits;
        }
    }

    /// <summary>Returns <see langword="true"/> while the cooldown period has not yet expired.</summary>
    private bool IsCooldownActive(long nowTicks) =>
        _cooldownStartTicks != 0L
        && (nowTicks - _cooldownStartTicks) < _cooldownDuration.Ticks;

    /// <summary>
    /// Records an outbound send in the bus event window and updates the last-send timestamp.
    /// </summary>
    private void RecordSendEvent(long nowTicks, int bits)
    {
        lock (_lock)
            _busEvents.Enqueue((nowTicks, bits));
        _lastSendTicks = nowTicks;
    }
}
