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

    private KnxBusRateLimiter CreateLimiter(int busBitRate = 9600, int avgBits = 200, int lineCount = 1)
    {
        var options = new KnxIpRoutingOptions
        {
            BusBitRate           = busBitRate,
            AverageTelegramBits  = avgBits,
            BusLineCount         = lineCount,
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

    // ---- First send ----

    [Test]
    public async Task WaitForSendSlotAsync_FirstCall_CompletesImmediately()
    {
        var limiter = CreateLimiter();
        await limiter.WaitForSendSlotAsync(CancellationToken.None);
        // No assertion needed — verifies it doesn't block or throw
    }

    // ---- Delay on second send within interval ----

    [Test]
    public async Task WaitForSendSlotAsync_SecondSendWithinInterval_IsDelayed()
    {
        var limiter = CreateLimiter();
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None); // sets lastEvent = T0

        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);

        _timeProvider.Advance(interval / 2); // not enough
        Assert.That(task.IsCompleted, Is.False, "Send should still be pending — interval not yet elapsed");

        _timeProvider.Advance(interval); // now past the full interval
        await task;
    }

    [Test]
    public async Task WaitForSendSlotAsync_SendAfterIntervalElapsed_CompletesImmediately()
    {
        var limiter = CreateLimiter();
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None);
        _timeProvider.Advance(interval + TimeSpan.FromMilliseconds(1));

        // Should not be delayed
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        await task;
    }

    // ---- Received telegrams count toward rate ----

    [Test]
    public async Task WaitForSendSlotAsync_AfterNotifyReceived_IsDelayed()
    {
        var limiter = CreateLimiter();
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        limiter.NotifyReceived(); // incoming telegram at T0

        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);

        _timeProvider.Advance(interval / 2);
        Assert.That(task.IsCompleted, Is.False, "Send should be delayed because a received telegram occupied the bus");

        _timeProvider.Advance(interval);
        await task;
    }

    [Test]
    public void NotifyReceived_IsNonBlocking_DoesNotAcquireSendGate()
    {
        var limiter = CreateLimiter();

        // Multiple calls must not block or consume the semaphore
        limiter.NotifyReceived();
        limiter.NotifyReceived();
        limiter.NotifyReceived();

        // The send gate must still be acquirable immediately after
        var task = limiter.WaitForSendSlotAsync(CancellationToken.None);
        _timeProvider.Advance(new KnxIpRoutingOptions().MinTelegramInterval * 2);
        Assert.DoesNotThrowAsync(async () => await task);
    }

    // ---- Cancellation ----

    [Test]
    public async Task WaitForSendSlotAsync_CancelledWhileWaiting_ThrowsOperationCanceledException()
    {
        var limiter = CreateLimiter();
        await limiter.WaitForSendSlotAsync(CancellationToken.None); // first: sets lastEvent

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
        var limiter = CreateLimiter();
        var interval = new KnxIpRoutingOptions().MinTelegramInterval;

        await limiter.WaitForSendSlotAsync(CancellationToken.None); // first: immediate

        // Two more senders racing
        var task1 = limiter.WaitForSendSlotAsync(CancellationToken.None);
        var task2 = limiter.WaitForSendSlotAsync(CancellationToken.None);

        Assert.That(task1.IsCompleted, Is.False, "task1 must wait for the interval");
        Assert.That(task2.IsCompleted, Is.False, "task2 must also wait");

        // Advance enough for task1 to complete its interval
        _timeProvider.Advance(interval * 2);
        await task1;

        // task2 queued behind task1; task1 set a new lastEvent so task2 still needs a full interval
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
