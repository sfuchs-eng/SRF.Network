namespace SRF.Network.Knx.Connection;

/// <summary>
/// Token-bucket rate limiter for KNX bus access with sliding-window load awareness
/// and burst-with-cooldown behavior.
/// <para>
/// <b>Normal mode</b> (bus load ≤ <c>MaxContinuousBusLoad</c>): tokens accumulate at one per
/// <see cref="KnxIpRoutingOptions.MinTelegramInterval"/>, capped at
/// <see cref="KnxIpRoutingOptions.MaxBurstSize"/>. Outbound sends wait until a token is available.
/// </para>
/// <para>
/// <b>High-load mode</b> (bus load &gt; <c>MaxContinuousBusLoad</c>): only existing burst tokens may
/// be spent — the rate limiter does not generate new tokens by waiting. When the burst pool is
/// exhausted the limiter transitions to cooldown.
/// </para>
/// <para>
/// <b>Cooldown mode</b>: sends are spaced at <c>MinTelegramInterval / CooldownMaxBusLoad</c> apart
/// for <see cref="KnxIpRoutingOptions.CooldownDuration"/>, after which normal mode resumes.
/// </para>
/// <para>
/// Received telegrams (via <see cref="NotifyReceived"/>) are recorded in the load window only.
/// They do not consume tokens or block pending sends, but they do increase the measured bus load
/// and can trigger high-load or cooldown transitions.
/// </para>
/// </summary>
internal sealed class KnxBusRateLimiter
{
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _loadWindowDuration;
    private readonly int _maxBurstSize;
    private readonly double _maxContinuousLoad;
    private readonly TimeSpan _cooldownDuration;
    private readonly double _cooldownMaxLoad;

    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    // Bus event window (sends + receives) — guarded by _lock
    private readonly Queue<long> _busEventTicks = new();
    private readonly object _lock = new();

    // Token bucket — only accessed while holding _sendGate (single-writer), so no extra lock needed
    private double _availableTokens;       // 0 .. _maxBurstSize
    private long _lastRefillTicks;         // when tokens were last topped up
    private long _lastSendTicks;           // when the last send event was recorded (0 = never)
    private long _cooldownStartTicks;      // 0 = not in cooldown

    internal KnxBusRateLimiter(KnxIpRoutingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _minInterval         = options.MinTelegramInterval;
        _loadWindowDuration  = options.LoadWindowDuration;
        _maxBurstSize        = Math.Max(options.MaxBurstSize, 1);
        _maxContinuousLoad   = options.MaxContinuousBusLoad;
        _cooldownDuration    = options.CooldownDuration;
        _cooldownMaxLoad     = options.CooldownMaxBusLoad;

        _timeProvider    = timeProvider;
        _availableTokens = _maxBurstSize; // start with a full bucket
    }

    /// <summary>
    /// Records an incoming telegram in the bus load window.
    /// Non-blocking and thread-safe; does not consume tokens or block pending sends.
    /// </summary>
    internal void NotifyReceived()
    {
        var now = _timeProvider.GetUtcNow().UtcTicks;
        lock (_lock)
            _busEventTicks.Enqueue(now);
    }

    /// <summary>
    /// Waits until the bus rate allows the next outbound send, then claims the send slot.
    /// Concurrent callers are serialized; each evaluates the current load independently after
    /// the previous sender has finished.
    /// </summary>
    internal async Task WaitForSendSlotAsync(CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_minInterval == TimeSpan.Zero)
            {
                // Rate limiting disabled — record send and return immediately.
                RecordSendEvent(_timeProvider.GetUtcNow().UtcTicks);
                return;
            }

            var delay = ComputeDelay();

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                // Refill tokens for the time we just waited.
                RefillTokens(_timeProvider.GetUtcNow().UtcTicks);
            }

            // Consume one token and record the send.
            _availableTokens = Math.Max(0.0, _availableTokens - 1.0);
            RecordSendEvent(_timeProvider.GetUtcNow().UtcTicks);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    // ---- Private helpers ----

    /// <summary>
    /// Computes the delay the current caller must wait before being allowed to send.
    /// Also transitions state (entering cooldown) as a side effect when needed.
    /// Must be called while holding the send gate.
    /// </summary>
    private TimeSpan ComputeDelay()
    {
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        RefillTokens(nowTicks);

        var load = ComputeLoad(nowTicks);

        // --- Cooldown active ---
        if (IsCooldownActive(nowTicks))
            return CooldownSendDelay(nowTicks);

        // --- Normal mode: load at or below threshold ---
        if (load <= _maxContinuousLoad)
        {
            if (_availableTokens >= 1.0)
                return TimeSpan.Zero;

            // Wait for one token to become available.
            return TimeSpan.FromTicks((long)((1.0 - _availableTokens) * _minInterval.Ticks));
        }

        // --- High-load mode: load above threshold ---
        if (_availableTokens >= 1.0)
            return TimeSpan.Zero; // spend a burst token; no delay

        // Burst pool exhausted under high load → enter cooldown.
        _cooldownStartTicks = nowTicks;
        return CooldownSendDelay(nowTicks);
    }

    /// <summary>
    /// Returns the delay required in cooldown mode so that sends do not exceed
    /// <see cref="_cooldownMaxLoad"/> of bus capacity.
    /// </summary>
    private TimeSpan CooldownSendDelay(long nowTicks)
    {
        if (_cooldownMaxLoad <= 0.0)
        {
            // Completely paused — wait until cooldown expires, then resume with zero delay.
            var cooldownRemaining = _cooldownStartTicks + _cooldownDuration.Ticks - nowTicks;
            return cooldownRemaining > 0 ? TimeSpan.FromTicks(cooldownRemaining) : TimeSpan.Zero;
        }

        var cooldownInterval = TimeSpan.FromTicks((long)(_minInterval.Ticks / _cooldownMaxLoad));

        if (_lastSendTicks == 0L)
            return TimeSpan.Zero;

        var elapsed = TimeSpan.FromTicks(nowTicks - _lastSendTicks);
        var remaining = cooldownInterval - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Adds tokens for the elapsed time since the last refill. Capped at <see cref="_maxBurstSize"/>.
    /// Must be called while holding the send gate (single-writer).
    /// </summary>
    private void RefillTokens(long nowTicks)
    {
        if (_lastRefillTicks == 0L)
        {
            _lastRefillTicks = nowTicks;
            return;
        }

        var elapsedTicks = nowTicks - _lastRefillTicks;
        if (elapsedTicks <= 0)
            return;

        var newTokens = elapsedTicks / (double)_minInterval.Ticks;
        _availableTokens = Math.Min(_availableTokens + newTokens, _maxBurstSize);
        _lastRefillTicks = nowTicks;
    }

    /// <summary>
    /// Returns the bus load (0–1) measured over the <see cref="_loadWindowDuration"/> sliding window.
    /// Prunes events older than the window as a side effect.
    /// </summary>
    private double ComputeLoad(long nowTicks)
    {
        if (_loadWindowDuration == TimeSpan.Zero)
            return 0.0;

        var cutoff = nowTicks - _loadWindowDuration.Ticks;
        lock (_lock)
        {
            while (_busEventTicks.Count > 0 && _busEventTicks.Peek() < cutoff)
                _busEventTicks.Dequeue();

            return (double)_busEventTicks.Count / _maxBurstSize;
        }
    }

    /// <summary>Returns <see langword="true"/> while the cooldown period has not yet expired.</summary>
    private bool IsCooldownActive(long nowTicks) =>
        _cooldownStartTicks != 0L
        && (nowTicks - _cooldownStartTicks) < _cooldownDuration.Ticks;

    /// <summary>
    /// Records an outbound send in both the bus event window and the last-send timestamp.
    /// </summary>
    private void RecordSendEvent(long nowTicks)
    {
        lock (_lock)
            _busEventTicks.Enqueue(nowTicks);
        _lastSendTicks = nowTicks;
    }
}

