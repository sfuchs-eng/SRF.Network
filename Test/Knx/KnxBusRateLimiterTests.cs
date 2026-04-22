using Microsoft.Extensions.Time.Testing;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;

namespace SRF.Network.Test.Knx;

[TestFixture]
public class KnxBusRateLimiterTests
{
    private FakeTimeProvider _timeProvider = null!;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // A "standard" telegram: 11 cEMI bytes * 11 bits/byte + 20 overhead = 141 bits.
    // For simpler test math we use a round 200-bit telegram throughout, matching AverageTelegramBits.
    private const int StandardBits = 200;

    [SetUp]
    public void SetUp() => _timeProvider = new FakeTimeProvider(T0);

    private KnxBusRateLimiter CreateLimiter(
        int busBitRate            = 9600,
        int lineCount             = 1,
        int maxBurstBits          = 200,    // 1 standard telegram by default (easy math)
        double maxContinuousLoad  = 0.5,
        TimeSpan? cooldownDuration = null,
        double cooldownMaxLoad    = 0.2)
    {
        var options = new KnxIpRoutingOptions
        {
            BusBitRate                    = busBitRate,
            BusLineCount                  = lineCount,
            MaxBurstBits                  = maxBurstBits,
            MaxContinuousBusLoad          = maxContinuousLoad,
            CooldownDuration              = cooldownDuration ?? TimeSpan.FromSeconds(1),
            CooldownMaxBusLoad            = cooldownMaxLoad,
            // Keep AverageTelegramBits at default so MinTelegramInterval tests still work
        };
        return new KnxBusRateLimiter(options, _timeProvider);
    }

    // Helper: duration to transmit `bits` at full bus speed
    private static TimeSpan TxTime(int bits, int busBitRate = 9600, int lineCount = 1) =>
        TimeSpan.FromSeconds(bits / (double)(busBitRate * lineCount));

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
    public void MinTelegramInterval_ZeroAverageTelegramBits_ReturnsZero() =>
        Assert.That(new KnxIpRoutingOptions { AverageTelegramBits = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));

    [Test]
    public void MinTelegramInterval_ZeroBusBitRate_ReturnsZero() =>
        Assert.That(new KnxIpRoutingOptions { BusBitRate = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));

    [Test]
    public void MinTelegramInterval_ZeroLineCount_ReturnsZero() =>
        Assert.That(new KnxIpRoutingOptions { BusLineCount = 0 }.MinTelegramInterval, Is.EqualTo(TimeSpan.Zero));

    [Test]
    public void LoadWindowDuration_DefaultOptions_IsMaxBurstBitsDividedByBusBitRate()
    {
        var options = new KnxIpRoutingOptions();
        var expected = TimeSpan.FromSeconds(options.MaxBurstBits / (double)(options.BusBitRate * options.BusLineCount));
        Assert.That(options.LoadWindowDuration, Is.EqualTo(expected));
    }

    [Test]
    public void LoadWindowDuration_WhenRateLimitingDisabled_ReturnsZero() =>
        Assert.That(new KnxIpRoutingOptions { BusBitRate = 0 }.LoadWindowDuration, Is.EqualTo(TimeSpan.Zero));

    [Test]
    public void ComputeTelegramBits_SmallPayload_MatchesFormula()
    {
        var options = new KnxIpRoutingOptions(); // BitsPerByte=11, Overhead=20
        // UDP payload = 6 (KNX/IP header) + 11 (min cEMI) = 17 bytes
        // cEMI bytes = 17 - 6 = 11; bits = 11*11 + 20 = 141
        Assert.That(options.ComputeTelegramBits(17), Is.EqualTo(141));
    }

    [Test]
    public void ComputeTelegramBits_LargePayload_IncludesExtraBytes()
    {
        var options = new KnxIpRoutingOptions();
        // 17 + 2 extra data bytes = 19 bytes UDP; cEMI = 13 bytes; bits = 13*11 + 20 = 163
        Assert.That(options.ComputeTelegramBits(19), Is.EqualTo(163));
    }

    [Test]
    public void ComputeTelegramBits_VeryShortPayload_UsesOverheadOnly()
    {
        var options = new KnxIpRoutingOptions();
        // Payload shorter than 6 → cEMI bytes = 0; bits = 0*11 + 20 = 20
        Assert.That(options.ComputeTelegramBits(4), Is.EqualTo(20));
    }

    // ---- First send ----

    [Test]
    public async Task WaitForSendSlotAsync_FirstCall_CompletesImmediately()
    {
        var limiter = CreateLimiter();
        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        // No assertion needed — verifies it does not block or throw
    }

    // ---- Token bucket: burst sends ----

    [Test]
    public async Task WaitForSendSlotAsync_NRapidSends_AllCompleteWithoutDelay_WhenBurstCoversAll()
    {
        // maxBurstBits=5*StandardBits so 5 sends are all immediate.
        var limiter = CreateLimiter(maxBurstBits: 5 * StandardBits, maxContinuousLoad: 1.0);

        for (int i = 0; i < 5; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Send {i + 1} of 5 should be immediate (burst credit available)");
            await task;
        }
    }

    [Test]
    public async Task WaitForSendSlotAsync_SendExceedingBurstCredit_WaitsForTokenRefill()
    {
        // 1-telegram bucket: second send must wait one TxTime.
        var limiter  = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 1.0);
        var txTime   = TxTime(StandardBits);

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);

        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "Second send must wait — burst credit exhausted");

        _timeProvider.Advance(txTime / 2); // not enough credit
        Assert.That(task.IsCompleted, Is.False, "Still not enough credit");

        _timeProvider.Advance(txTime); // now past full interval
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_LargerTelegramWaitsProportionallyLonger()
    {
        // A 2x telegram requires 2x credit — takes twice as long to accumulate.
        var limiter  = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 1.0);
        var txTime1x = TxTime(StandardBits);
        var txTime2x = TxTime(2 * StandardBits);

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // empties bucket

        var task = limiter.WaitForSendSlotAsync(2 * StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False);

        _timeProvider.Advance(txTime1x); // only 1x of credit returned — not enough for 2x
        Assert.That(task.IsCompleted, Is.False, "1x credit still insufficient for 2x telegram");

        _timeProvider.Advance(txTime2x); // enough credit accumulated
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_SendAfterCreditRefilled_CompletesImmediately()
    {
        var limiter = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 1.0);
        var txTime  = TxTime(StandardBits);

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        _timeProvider.Advance(txTime + TimeSpan.FromMilliseconds(1));

        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        await task; // should not be delayed
    }

    [Test]
    public async Task WaitForSendSlotAsync_BucketRefillsOverTime_AllowsNewBurst()
    {
        const int burst = 5;
        var limiter = CreateLimiter(maxBurstBits: burst * StandardBits, maxContinuousLoad: 1.0);
        var txTime  = TxTime(StandardBits);

        // Exhaust the full bucket.
        for (int i = 0; i < burst; i++)
            await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);

        // Advance enough to refill completely (add 1 ms margin for floating-point precision).
        _timeProvider.Advance(TimeSpan.FromTicks(txTime.Ticks * burst) + TimeSpan.FromMilliseconds(1));

        // Should burst again without delay.
        for (int i = 0; i < burst; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Refilled burst send {i + 1} should be immediate");
            await task;
        }
    }

    // ---- NotifyReceived: contributes to load window, does NOT block sends ----

    [Test]
    public async Task WaitForSendSlotAsync_AfterNotifyReceived_DoesNotDelayWhileBurstAvailable()
    {
        var limiter = CreateLimiter(maxBurstBits: 5 * StandardBits);

        limiter.NotifyReceived(StandardBits); // incoming telegram

        // Burst credit still available — receive must not block send.
        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "Burst credit available — received telegram must not delay send");
        await task;
    }

    [Test]
    public void NotifyReceived_IsNonBlocking_DoesNotAcquireSendGate()
    {
        var limiter = CreateLimiter();

        limiter.NotifyReceived(StandardBits);
        limiter.NotifyReceived(StandardBits);
        limiter.NotifyReceived(StandardBits);

        // Send gate must still be acquirable immediately.
        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "Send gate must be acquirable after NotifyReceived calls");
        Assert.DoesNotThrowAsync(async () => await task);
    }

    [Test]
    public async Task NotifyReceived_MultipleCalls_ContributeToLoadWindow()
    {
        // Saturate load window with received-only events.
        // With maxContinuousLoad=0.0 the limiter is always in high-load mode.
        // Burst credit covers the sends → still no delay.
        const int burst = 5;
        var limiter = CreateLimiter(maxBurstBits: burst * StandardBits, maxContinuousLoad: 0.0);

        for (int i = 0; i < burst; i++)
        {
            limiter.NotifyReceived(StandardBits);
            _timeProvider.Advance(TimeSpan.FromTicks(1));
        }

        // High-load mode, but burst credit available → sends are still immediate.
        for (int i = 0; i < burst; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"High-load burst send {i + 1} should be immediate");
            await task;
        }
    }

    [Test]
    public async Task NotifyReceived_EventsOlderThanLoadWindow_ArePruned()
    {
        const int burst = 5;
        var options        = new KnxIpRoutingOptions { MaxBurstBits = burst * StandardBits };
        var windowDuration = options.LoadWindowDuration;
        var limiter        = CreateLimiter(maxBurstBits: burst * StandardBits, maxContinuousLoad: 0.5);

        // Saturate the window.
        for (int i = 0; i < burst; i++)
        {
            limiter.NotifyReceived(StandardBits);
            _timeProvider.Advance(TimeSpan.FromTicks(1));
        }

        // Advance past window → old events pruned → load drops to 0.
        _timeProvider.Advance(windowDuration + TimeSpan.FromMilliseconds(1));

        // Load is now 0 (normal mode), bucket full → sends immediate.
        for (int i = 0; i < burst; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Send {i + 1} should be immediate after load window expired");
            await task;
        }
    }

    // ---- High-load mode ----

    [Test]
    public async Task WaitForSendSlotAsync_HighLoad_SpendsBurstCreditWithoutDelay()
    {
        // maxContinuousLoad=0.0 → always high-load; burst credit covers all sends.
        const int burst = 5;
        var limiter = CreateLimiter(maxBurstBits: burst * StandardBits, maxContinuousLoad: 0.0);

        for (int i = 0; i < burst; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"High-load burst send {i + 1} should be immediate");
            await task;
        }
    }

    // ---- Cooldown mode ----

    [Test]
    public async Task WaitForSendSlotAsync_BurstDepletedUnderHighLoad_TriggersCooldown()
    {
        // Always high-load (maxContinuousLoad=0.0). After exhausting 1-telegram bucket
        // the next send enters cooldown at 20% load → spaced at TxTime(bits) / 0.2.
        var limiter            = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 0.0,
                                               cooldownDuration: TimeSpan.FromSeconds(1), cooldownMaxLoad: 0.2);
        var cooldownTxTime     = TxTime(StandardBits) / 0.2; // 5× normal

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // uses burst

        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "Should enter cooldown and be delayed");

        _timeProvider.Advance(cooldownTxTime);
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_InCooldown_SendsSpacedAtCooldownInterval()
    {
        var limiter        = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 0.0,
                                            cooldownDuration: TimeSpan.FromSeconds(10), cooldownMaxLoad: 0.2);
        var cooldownTxTime = TxTime(StandardBits) / 0.2;

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // exhaust burst

        // First cooldown send.
        var task1 = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        _timeProvider.Advance(cooldownTxTime);
        await task1;

        // Second cooldown send must also wait a full cooldown interval.
        var task2 = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        _timeProvider.Advance(cooldownTxTime / 2); // not enough
        Assert.That(task2.IsCompleted, Is.False, "Second cooldown send must still wait");
        _timeProvider.Advance(cooldownTxTime);
        await task2;
    }

    [Test]
    public async Task WaitForSendSlotAsync_CooldownExpires_ReturnsToNormalMode()
    {
        const int burst = 5;
        var cooldownDur   = TimeSpan.FromMilliseconds(200);
        var options       = new KnxIpRoutingOptions { MaxBurstBits = burst * StandardBits };
        var windowDuration = options.LoadWindowDuration;
        var limiter       = CreateLimiter(maxBurstBits: burst * StandardBits, maxContinuousLoad: 0.0,
                                           cooldownDuration: cooldownDur, cooldownMaxLoad: 0.2);

        // Exhaust burst → triggers cooldown.
        for (int i = 0; i < burst; i++)
            await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);

        // Advance past cooldown + load window so load drops and cooldown expires.
        _timeProvider.Advance(cooldownDur + windowDuration + TimeSpan.FromMilliseconds(1));

        // Back to normal mode (load=0, maxContinuousLoad=0.0 → 0 <= 0.0 is true → normal).
        // Bucket has also refilled → sends immediate.
        for (int i = 0; i < burst; i++)
        {
            var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, $"Post-cooldown burst send {i + 1} should be immediate");
            await task;
        }
    }

    [Test]
    public async Task WaitForSendSlotAsync_CooldownWithZeroMaxLoad_PausesSendingUntilCooldownExpires()
    {
        var cooldownDur = TimeSpan.FromMilliseconds(500);
        var limiter     = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 0.0,
                                         cooldownDuration: cooldownDur, cooldownMaxLoad: 0.0);

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // exhaust burst

        var task = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "Should be paused during zero-load cooldown");

        _timeProvider.Advance(cooldownDur - TimeSpan.FromMilliseconds(1));
        Assert.That(task.IsCompleted, Is.False, "Still paused before cooldown expires");

        _timeProvider.Advance(TimeSpan.FromMilliseconds(2));
        await task;
    }

    // ---- Cancellation ----

    [Test]
    public async Task WaitForSendSlotAsync_CancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        var limiter = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 1.0);
        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // exhaust bucket

        using var cts = new CancellationTokenSource();
        var task = limiter.WaitForSendSlotAsync(StandardBits, cts.Token);

        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    [Test]
    public void WaitForSendSlotAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var limiter = CreateLimiter();
        var task = limiter.WaitForSendSlotAsync(StandardBits, new CancellationToken(canceled: true));
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    // ---- Concurrent senders are serialized ----

    [Test]
    public async Task WaitForSendSlotAsync_ConcurrentSenders_AreSerializedThroughSendGate()
    {
        // 1-telegram bucket + normal mode so each sender waits exactly one TxTime.
        var limiter = CreateLimiter(maxBurstBits: StandardBits, maxContinuousLoad: 1.0);
        var txTime  = TxTime(StandardBits);

        await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None); // uses bucket

        var task1 = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
        var task2 = limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);

        Assert.That(task1.IsCompleted, Is.False, "task1 must wait for credit");
        Assert.That(task2.IsCompleted, Is.False, "task2 must also wait");

        _timeProvider.Advance(txTime * 2);
        await task1;

        _timeProvider.Advance(txTime * 2);
        await task2;
    }

    // ---- Zero bit rate (rate limiting disabled) ----

    [Test]
    public async Task WaitForSendSlotAsync_ZeroBitRate_AllSendsCompleteWithoutDelay()
    {
        var options = new KnxIpRoutingOptions { BusBitRate = 0 };
        var limiter = new KnxBusRateLimiter(options, _timeProvider);

        for (int i = 0; i < 5; i++)
            await limiter.WaitForSendSlotAsync(StandardBits, CancellationToken.None);
    }
}
