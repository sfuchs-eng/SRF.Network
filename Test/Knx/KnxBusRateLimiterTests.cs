using Microsoft.Extensions.Time.Testing;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;

namespace SRF.Network.Test.Knx;

[TestFixture]
public class KnxBusRateLimiterTests
{
    private FakeTimeProvider _timeProvider = null!;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp() => _timeProvider = new FakeTimeProvider(T0);

    private KnxBusRateLimiter CreateLimiter(
        int busBitRate             = 9600,
        int avgBits                = 200,
        int lineCount              = 1,
        int maxBurstSize           = 5,
        double maxContinuousLoad   = 0.5,
        TimeSpan? cooldownDuration = null,
        double cooldownMaxLoad     = 0.2)
    {
        var options = new KnxIpRoutingOptions
        {
            BusBitRate           = busBitRate,
            AverageTelegramBits  = avgBits,
            BusLineCount         = lineCount,
            MaxBurstSize         = maxBurstSize,
            MaxContinuousBusLoad = maxContinuousLoad,
            CooldownDuration     = cooldownDuration ?? TimeSpan.FromSeconds(1),
            CooldownMaxBusLoad   = cooldownMaxLoad,
        };
        return new KnxBusRateLimiter(options, _timeProvider);
    }

    // ---- Options / computed interval ----

    [Test]
    public void MinTelegramInterval_DefaultOptions_IsComputedFromBusPhysics()
    {
        var options = new KnxIpRoutingOptions();
        var expected = TimeSpan.FromSeconds(200.0 / 9600.0);
        Assert.That(options.MinTelegramInterval, Is.EqualTo(expected));
    }

    [Test]
    public void MinTelegramInterval_TwoLines_IsHalfOfSingleLine()
    {
        var single = new KnxIpRoutingOptions { BusLineCount = 1 }.MinTelegramInterval;
        var dual   = new KnxIpRoutingOptions { BusLineCount = 2 }.MinTelegramInterval;
        Assert.That(dual, Is.EqualTo(TimeSpan.FromTicks(single.Ticks / 2)));
    }

    [Test]
    public void MinTelegramInterval_ZeroAverageTelegramBits_ReturnsZero()
    {
        Assert.That(new KnxIpRoutingOptions { AverageTelegramBits = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void MinTelegramInterval_ZeroBusBitRate_ReturnsZero()
    {
        Assert.That(new KnxIpRoutingOptions { BusBitRate = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void MinTelegramInterval_ZeroLineCount_ReturnsZero()
    {
        Assert.That(new KnxIpRoutingOptions { BusLineCount = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void LoadWindowDuration_DefaultOptions_IsMaxBurstSizeTimesMinInterval()
    {
        var options = new KnxIpRoutingOptions();
        var expected = TimeSpan.FromTicks(options.MaxBurstSize * options.MinTelegramInterval.Ticks);
        Assert.That(options.LoadWindowDuration, Is.EqualTo(expected));
    }

    [Test]
    public void LoadWindowDuration_WhenRateLimitingDisabled_ReturnsZero()
    {
        Assert.That(new KnxIpRoutingOptions { AverageTelegramBits = 0 }.LoadWindowDuration, Is.EqualTo(TimeSpan.Zero));
    }

    // ---- First send ----

    [Test]
    public async Task WaitForSendSlotAsync_FirstCall_CompletesImmediately()
    {
        var limiter = CreateLimiter();
        await limiter.WaitForSendSlotAsync(CancellationToken.None);
        // No assertion needed — verifies it doesn't block or throw
    }

    // ---- Token bucket: burst sends ----

    [Test]
    public async Task WaitForSendSlotAsync_NRapidSends_AllCompleteWithoutDelay()
    {
        // MaxBurstSize=5, so 5 rapid sends should all be immediate.
        var limiter = CreateLimiter(maxBurstSize: 5);

        for (int i = 0; i < 5; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Send {i + 1} of 5 should be immediate (burst token available)");
            await task;
        }
    }

    [Test]
    public async Task WaitForSendSlotAsync_NPlus1thSend_WaitsForTokenRefill()
    {
        // After exhausting all 5 burst tokens, the 6th send must wait one MinInterval for a token refill.
        // maxContinuousLoad=1.0 keeps the limiter in normal token-bucket mode so the wait is exactly
        // one MinInterval, not a longer cooldown interval triggered by high-load detection.
        var limiter   = CreateLimiter(maxBurstSize: 5, maxContinuousLoad: 1.0);
        var interval  = new KnxIpRoutingOptions().MinTelegramInterval;

        for (int i = 0; i < 5; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "6th send must wait — burst exhausted");

        _timeProvider.Advance(interval);
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_TokensRefillOverTime_AllowsSubsequentSendWithoutDelay()
    {
        // After using all burst tokens and waiting MaxBurstSize intervals, bucket is full again.
        var limiter      = CreateLimiter(maxBurstSize: 5);
        var interval     = new KnxIpRoutingOptions().MinTelegramInterval;
        var maxBurstSize = 5;

        for (int i = 0; i < maxBurstSize; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        // Advance by MaxBurstSize intervals so the bucket refills completely.
        _timeProvider.Advance(interval * maxBurstSize);

        // Next burst of sends must again be immediate.
        for (int i = 0; i < maxBurstSize; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Refilled burst send {i + 1} should be immediate");
            await task;
        }
    }

    // ---- Delay on second send within interval (single-token bucket) ----

    [Test]
    public async Task WaitForSendSlotAsync_SecondSendWithinInterval_IsDelayed()
    {
        // maxBurstSize=1 so the single token is consumed by the first send.
        // maxContinuousLoad=1.0 keeps the limiter in normal token-bucket mode (not high-load/cooldown)
        // so the second send waits exactly one MinTelegramInterval for a token to refill.
        var limiter  = CreateLimiter(maxBurstSize: 1, maxContinuousLoad: 1.0);
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None); // uses the single token

        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);

        _timeProvider.Advance(interval / 2); // not enough
        Assert.That(task.IsCompleted, Is.False, "Send should still be pending — interval not yet elapsed");

        _timeProvider.Advance(interval); // now past the full interval
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_SendAfterIntervalElapsed_CompletesImmediately()
    {
        var limiter  = CreateLimiter(maxBurstSize: 1);
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None);
        _timeProvider.Advance(interval + TimeSpan.FromMilliseconds(1));

        // Token has refilled — should not be delayed.
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        await task;
    }

    // ---- NotifyReceived: contributes to load window, does NOT block sends ----

    [Test]
    public async Task WaitForSendSlotAsync_AfterNotifyReceived_DoesNotDelayWhileBurstAvailable()
    {
        // Received telegrams no longer directly block sending when burst tokens remain.
        var limiter  = CreateLimiter(maxBurstSize: 5);
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        limiter.NotifyReceived(); // one received telegram at T0

        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "Burst token available — received telegram must not delay send");
        await task;
    }

    [Test]
    public void NotifyReceived_IsNonBlocking_DoesNotAcquireSendGate()
    {
        var limiter = CreateLimiter();

        // Multiple calls must not block or consume the semaphore.
        limiter.NotifyReceived();
        limiter.NotifyReceived();
        limiter.NotifyReceived();

        // The send gate must still be acquirable immediately after.
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "Send gate must be acquirable after NotifyReceived calls");
        Assert.DoesNotThrowAsync(async () => await task);
    }

    [Test]
    public async Task NotifyReceived_MultipleCalls_ContributeToLoadWindow()
    {
        // Fill the load window with received-only events. With load > MaxContinuousBusLoad and
        // burst tokens available the sends should still go through (high-load mode, spend tokens).
        var maxBurstSize  = 5;
        var limiter       = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.5);
        var interval      = new KnxIpRoutingOptions { MaxBurstSize = maxBurstSize }.MinTelegramInterval;
        var windowDuration = new KnxIpRoutingOptions { MaxBurstSize = maxBurstSize }.LoadWindowDuration;

        // Fire MaxBurstSize received telegrams — load window is now saturated.
        for (int i = 0; i < maxBurstSize; i++)
        {
            limiter.NotifyReceived();
            _timeProvider.Advance(TimeSpan.FromTicks(1)); // ensure distinct ticks
        }

        // Sends should spend burst tokens (high-load mode) — still no blocking delay.
        for (int i = 0; i < maxBurstSize; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"High-load burst send {i + 1} should be immediate");
            await task;
        }
    }

    [Test]
    public async Task NotifyReceived_EventsOlderThanLoadWindow_ArePruned()
    {
        var maxBurstSize   = 5;
        var limiter        = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.5);
        var windowDuration = new KnxIpRoutingOptions { MaxBurstSize = maxBurstSize }.LoadWindowDuration;

        // Saturate the window with received telegrams.
        for (int i = 0; i < maxBurstSize; i++)
        {
            limiter.NotifyReceived();
            _timeProvider.Advance(TimeSpan.FromTicks(1));
        }

        // Advance past the window duration — old events must be pruned.
        _timeProvider.Advance(windowDuration + TimeSpan.FromMilliseconds(1));

        // Load window is empty now → normal mode, burst tokens full → immediate sends.
        for (int i = 0; i < maxBurstSize; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Send {i + 1} should be immediate after load window expired");
            await task;
        }
    }

    // ---- High-load mode ----

    [Test]
    public async Task WaitForSendSlotAsync_HighLoad_SpendsBurstTokensWithoutDelay()
    {
        // Load above threshold, but burst tokens available → high-load mode, sends immediate.
        var maxBurstSize = 5;
        var limiter      = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.0); // threshold=0 → always high-load

        for (int i = 0; i < maxBurstSize; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"High-load burst send {i + 1} should be immediate");
            await task;
        }
    }

    // ---- Cooldown mode ----

    [Test]
    public async Task WaitForSendSlotAsync_BurstDepletedUnderHighLoad_TriggersCooldown()
    {
        // With maxContinuousLoad=0 (always high-load), after MaxBurstSize sends the (N+1)th must wait.
        var maxBurstSize = 5;
        var limiter      = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.0,
                                         cooldownDuration: TimeSpan.FromSeconds(1), cooldownMaxLoad: 0.2);
        var interval     = new KnxIpRoutingOptions().MinTelegramInterval;

        for (int i = 0; i < maxBurstSize; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        // Next send should trigger cooldown → delayed (cooldown interval = minInterval / 0.2 = 5x minInterval)
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "Should enter cooldown and be delayed");

        // Advance by one cooldown-interval worth.
        _timeProvider.Advance(interval * 5); // minInterval / 0.2
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_InCooldown_SendsSpacedAtCooldownInterval()
    {
        var maxBurstSize = 5;
        var limiter      = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.0,
                                         cooldownDuration: TimeSpan.FromSeconds(10), cooldownMaxLoad: 0.2);
        var interval     = new KnxIpRoutingOptions().MinTelegramInterval;
        var cooldownInterval = TimeSpan.FromTicks((long)(interval.Ticks / 0.2));

        // Exhaust burst tokens to trigger cooldown.
        for (int i = 0; i < maxBurstSize; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        // First cooldown send: advance by cooldown interval.
        var task1 = limiter.WaitForSendSlotAsync(CancellationToken.None);
        _timeProvider.Advance(cooldownInterval);
        await task1;

        // Second cooldown send must also wait a full cooldown interval.
        var task2 = limiter.WaitForSendSlotAsync(CancellationToken.None);
        _timeProvider.Advance(cooldownInterval / 2); // not enough
        Assert.That(task2.IsCompleted, Is.False, "Second cooldown send must still wait");
        _timeProvider.Advance(cooldownInterval);
        await task2;
    }

    [Test]
    public async Task WaitForSendSlotAsync_CooldownExpires_ReturnsToNormalMode()
    {
        var maxBurstSize   = 5;
        var cooldownDur    = TimeSpan.FromMilliseconds(200);
        var limiter        = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.0,
                                            cooldownDuration: cooldownDur, cooldownMaxLoad: 0.2);
        var interval       = new KnxIpRoutingOptions().MinTelegramInterval;
        var windowDuration = new KnxIpRoutingOptions { MaxBurstSize = maxBurstSize }.LoadWindowDuration;
        var cooldownInterval = TimeSpan.FromTicks((long)(interval.Ticks / 0.2));

        // Exhaust burst tokens (enters cooldown).
        for (int i = 0; i < maxBurstSize; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        // Advance past the cooldown and load window so load drops and cooldown expires.
        _timeProvider.Advance(cooldownDur + windowDuration + TimeSpan.FromMilliseconds(1));

        // Now back to normal mode with maxContinuousLoad=0, all tokens should have refilled.
        // Since load is now 0 (window empty) and maxContinuousLoad=0.0, it falls into high-load
        // mode (0 > 0.0 is false, so normal mode). With a full bucket the sends should be immediate.
        for (int i = 0; i < maxBurstSize; i++)
        {
            var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Post-cooldown burst send {i + 1} should be immediate");
            await task;
        }
    }

    [Test]
    public async Task WaitForSendSlotAsync_CooldownWithZeroMaxLoad_PausesSendingUntilCooldownExpires()
    {
        var maxBurstSize = 5;
        var cooldownDur  = TimeSpan.FromMilliseconds(500);
        var limiter      = CreateLimiter(maxBurstSize: maxBurstSize, maxContinuousLoad: 0.0,
                                          cooldownDuration: cooldownDur, cooldownMaxLoad: 0.0);

        // Exhaust burst tokens.
        for (int i = 0; i < maxBurstSize; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);

        // Should be paused until cooldown expires.
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "Should be paused during zero-load cooldown");

        _timeProvider.Advance(cooldownDur - TimeSpan.FromMilliseconds(1));
        Assert.That(task.IsCompleted, Is.False, "Should still be paused before cooldown expires");

        _timeProvider.Advance(TimeSpan.FromMilliseconds(2));
        await task;
    }

    // ---- Cancellation ----

    [Test]
    public async Task WaitForSendSlotAsync_CancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        var limiter  = CreateLimiter(maxBurstSize: 1);
        await limiter.WaitForSendSlotAsync(CancellationToken.None); // use the single token

        using var cts = new CancellationTokenSource();
        var task = limiter.WaitForSendSlotAsync(cts.Token);

        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    [Test]
    public void WaitForSendSlotAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var limiter = CreateLimiter();
        var task = limiter.WaitForSendSlotAsync(new CancellationToken(canceled: true));
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    // ---- Concurrent senders are serialized ----

    [Test]
    public async Task WaitForSendSlotAsync_ConcurrentSenders_AreSerializedThroughSendGate()
    {
        // maxBurstSize=1 so each serialized sender must wait a full interval for the token to refill.
        // maxContinuousLoad=1.0 keeps the limiter in normal token-bucket mode so the wait is one
        // MinInterval, not a longer cooldown delay triggered by high-load detection.
        var limiter  = CreateLimiter(maxBurstSize: 1, maxContinuousLoad: 1.0);
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None); // first: immediate (uses token)

        // Two more senders racing.
        var task1 = limiter.WaitForSendSlotAsync(CancellationToken.None);
        var task2 = limiter.WaitForSendSlotAsync(CancellationToken.None);

        Assert.That(task1.IsCompleted, Is.False, "task1 must wait for the interval");
        Assert.That(task2.IsCompleted, Is.False, "task2 must also wait");

        // Advance enough for task1 to complete its interval.
        _timeProvider.Advance(interval * 2);
        await task1;

        // task2 queued behind task1; task1 set a new lastEvent so task2 still needs a full interval.
        _timeProvider.Advance(interval * 2);
        await task2;
    }

    // ---- Zero interval (rate limiting disabled) ----

    [Test]
    public async Task WaitForSendSlotAsync_ZeroInterval_AllSendsCompleteWithoutDelay()
    {
        var limiter = CreateLimiter(avgBits: 0);

        for (int i = 0; i < 5; i++)
            await limiter.WaitForSendSlotAsync(CancellationToken.None);
    }

}
