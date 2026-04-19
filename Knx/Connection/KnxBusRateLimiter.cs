namespace SRF.Network.Knx.Connection;

/// <summary>
/// Enforces a minimum interval between consecutive KNX bus events (sends and receives),
/// derived from the physical bus bandwidth configured in <see cref="KnxIpRoutingOptions"/>.
/// <para>
/// Outbound sends that would exceed the rate are delayed until the interval has elapsed.
/// Received telegrams also consume the rate budget (via <see cref="NotifyReceived"/>),
/// since they occupy bus time that cannot be controlled by this device.
/// </para>
/// <para>
/// Concurrent senders are serialized through an internal semaphore; each waits its own
/// minimum interval before the send slot is granted.
/// </para>
/// </summary>
internal sealed class KnxBusRateLimiter
{
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private long _lastBusEventTicks; // 0 = no event yet; accessed via Interlocked

    internal KnxBusRateLimiter(KnxIpRoutingOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _minInterval = options.MinTelegramInterval;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records an incoming telegram towards the combined bus rate budget.
    /// Non-blocking and thread-safe; safe to call from any thread.
    /// </summary>
    internal void NotifyReceived() =>
        Interlocked.Exchange(ref _lastBusEventTicks, _timeProvider.GetUtcNow().UtcTicks);

    /// <summary>
    /// Waits until the bus rate allows the next outbound send, then claims the send slot.
    /// Concurrent callers are serialized; each waits for its own interval after the previous event.
    /// </summary>
    internal async Task WaitForSendSlotAsync(CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_minInterval > TimeSpan.Zero)
            {
                long lastTicks = Interlocked.Read(ref _lastBusEventTicks);
                if (lastTicks != 0L)
                {
                    var last = new DateTimeOffset(lastTicks, TimeSpan.Zero);
                    var remaining = _minInterval - (_timeProvider.GetUtcNow() - last);
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }

            Interlocked.Exchange(ref _lastBusEventTicks, _timeProvider.GetUtcNow().UtcTicks);
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
