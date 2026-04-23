using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Messages;
using KnxConnEventArgs = SRF.Network.Knx.KnxConnectionEventArgs;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="KnxConnection"/>.
/// </summary>
[TestFixture]
public class KnxConnectionTests
{
    private IKnxBus _bus = null!;
    private IKnxLibraryInitialization _initializer = null!;
    private IDptResolver _dptResolver = null!;
    private KnxConnection _connection = null!;

    private sealed class StubDpt : DptBase
    {
        private readonly object _returnValue;
        [SetsRequiredMembers]
        public StubDpt(object returnValue = null!, int dptMain = 1, int dptSub = 1)
        {
            _returnValue = returnValue ?? 42;
            Id = new DataPointTypeId(dptMain, dptSub);
        }
        public override object ToValue(GroupValue groupValue) => _returnValue;
        public override GroupValue ToGroupValue(object value) => new([]);
    }

    private KnxConnection CreateConnection(bool busIsConnected = false, BusConnectionState connectionState = BusConnectionState.Closed)
    {
        _bus = Substitute.For<IKnxBus>();
        _bus.IsConnected.Returns(busIsConnected);
        _bus.ConnectionState.Returns(connectionState);

        _initializer = Substitute.For<IKnxLibraryInitialization>();
        _dptResolver = Substitute.For<IDptResolver>();

        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration());

        return new KnxConnection(
            _initializer,
            _bus,
            options,
            NullLogger<KnxConnection>.Instance,
            _dptResolver);
    }

    [SetUp]
    public void SetUp()
    {
        _connection = CreateConnection();
    }

    // -------------------------------------------------------------------------
    // IsConnected
    // -------------------------------------------------------------------------

    [Test]
    public void IsConnected_DelegatesToBus_WhenFalse()
    {
        _bus.IsConnected.Returns(false);
        Assert.That(_connection.IsConnected, Is.False);
    }

    [Test]
    public void IsConnected_DelegatesToBus_WhenTrue()
    {
        _bus.IsConnected.Returns(true);
        Assert.That(_connection.IsConnected, Is.True);
    }

    // -------------------------------------------------------------------------
    // ConnectAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task ConnectAsync_WhenNotConnected_CallsBusConnectAsync()
    {
        _bus.IsConnected.Returns(false);
        _bus.ConnectionState.Returns(BusConnectionState.Connected);
        _bus.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _connection.ConnectAsync();

        await _bus.Received(1).ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNotCallBusConnectAsync()
    {
        _bus.IsConnected.Returns(true);

        await _connection.ConnectAsync();

        await _bus.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectAsync_WhenBusConnects_SubscribesToMessageReceived()
    {
        _bus.IsConnected.Returns(false);
        _bus.ConnectionState.Returns(BusConnectionState.Connected);
        _bus.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _connection.ConnectAsync();

        // Verify that MessageReceived was subscribed by raising the event and checking the callback fires
        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        var eventArgs = new KnxMessageReceivedEventArgs(
            new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("0/0/1"),
                SourceAddress = new IndividualAddress("1.1.1"),
                EventType = GroupEventType.ValueRead,
                Value = new GroupValue([])
            },
            DateTimeOffset.UtcNow);

        _bus.MessageReceived += Raise.Event<EventHandler<KnxMessageReceivedEventArgs>>(null, eventArgs);

        Assert.That(captured, Is.Not.Null);
    }

    // -------------------------------------------------------------------------
    // DisconnectAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task DisconnectAsync_CallsBusDisconnectAsync()
    {
        _bus.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _connection.DisconnectAsync();

        await _bus.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // SendMessageAsync
    // -------------------------------------------------------------------------

    [Test]
    public void SendMessageAsync_NullMessage_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() =>
            _connection.SendMessageAsync(null!, CancellationToken.None));
    }

    [Test]
    public async Task SendMessageAsync_ValidMessage_DelegatesToBus()
    {
        var message = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]));
        _bus.SendGroupMessageAsync(message, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _connection.SendMessageAsync(message, CancellationToken.None);

        await _bus.Received(1).SendGroupMessageAsync(message, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // MessageReceived event forwarding with DPT resolution
    // -------------------------------------------------------------------------

    [Test]
    public async Task OnBusMessageReceived_WithResolvableDpt_ForwardsWithDecodedValue()
    {
        _bus.IsConnected.Returns(false);
        _bus.ConnectionState.Returns(BusConnectionState.Connected);
        _bus.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await _connection.ConnectAsync();

        var expectedDecodedValue = 42;
        var dpt = new StubDpt(expectedDecodedValue);
        var destAddress = new GroupAddress("0/0/1");
        _dptResolver.GetDpt(Arg.Any<GroupAddress>()).Returns(dpt);

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        var busEventArgs = new KnxMessageReceivedEventArgs(
            new GroupEventArgs
            {
                DestinationAddress = destAddress,
                SourceAddress = new IndividualAddress("1.1.1"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue([0x01])
            },
            DateTimeOffset.UtcNow);

        _bus.MessageReceived += Raise.Event<EventHandler<KnxMessageReceivedEventArgs>>(null, busEventArgs);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.KnxMessageContext.Dpt, Is.SameAs(dpt));
            Assert.That(captured.KnxMessageContext.DecodedValue, Is.EqualTo(expectedDecodedValue));
        });
    }

    [Test]
    public async Task OnBusMessageReceived_WhenDptResolutionFails_StillForwardsMessage()
    {
        _bus.IsConnected.Returns(false);
        _bus.ConnectionState.Returns(BusConnectionState.Connected);
        _bus.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await _connection.ConnectAsync();

        _dptResolver.GetDpt(Arg.Any<GroupAddress>()).Returns(_ => throw new KnxException("DPT not found"));

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        var busEventArgs = new KnxMessageReceivedEventArgs(
            new GroupEventArgs
            {
                DestinationAddress = new GroupAddress("9/9/9"),
                SourceAddress = new IndividualAddress("1.1.1"),
                EventType = GroupEventType.ValueWrite,
                Value = new GroupValue([0x01])
            },
            DateTimeOffset.UtcNow);

        _bus.MessageReceived += Raise.Event<EventHandler<KnxMessageReceivedEventArgs>>(null, busEventArgs);

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null, "MessageReceived should still fire even when DPT resolution fails");
            Assert.That(captured!.KnxMessageContext.Dpt, Is.Null, "Dpt should be null when resolution fails");
            Assert.That(captured.KnxMessageContext.DecodedValue, Is.Null, "DecodedValue should be null when resolution fails");
        });
    }

    [Test]
    public async Task OnBusMessageReceived_PreservesGroupEventArgs()
    {
        _bus.IsConnected.Returns(false);
        _bus.ConnectionState.Returns(BusConnectionState.Connected);
        _bus.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await _connection.ConnectAsync();

        var destAddress = new GroupAddress("1/2/3");
        var srcAddress = new IndividualAddress("2.1.5");
        var value = new GroupValue([0x0F]);
        var receivedAt = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

        _dptResolver.GetDpt(Arg.Any<GroupAddress>()).Returns(_ => throw new KnxException("not found"));

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        var busEventArgs = new KnxMessageReceivedEventArgs(
            new GroupEventArgs
            {
                DestinationAddress = destAddress,
                SourceAddress = srcAddress,
                EventType = GroupEventType.ValueWrite,
                Value = value
            },
            receivedAt);

        _bus.MessageReceived += Raise.Event<EventHandler<KnxMessageReceivedEventArgs>>(null, busEventArgs);

        Assert.Multiple(() =>
        {
            var ctx = captured!.KnxMessageContext;
            Assert.That(ctx.GroupEventArgs!.DestinationAddress.Address, Is.EqualTo(destAddress.Address));
            Assert.That(ctx.GroupEventArgs.SourceAddress.Address, Is.EqualTo(srcAddress.Address));
            Assert.That(ctx.GroupEventArgs.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(ctx.GroupEventArgs.Value.Value, Is.EqualTo(new byte[] { 0x0F }));
            Assert.That(ctx.ReceivedAt, Is.EqualTo(receivedAt));
        });
    }

    // -------------------------------------------------------------------------
    // ConnectionStatusChanged event forwarding
    // -------------------------------------------------------------------------

    [Test]
    public void ConnectionStatusChanged_ForwardedFromBus()
    {
        KnxConnEventArgs? captured = null;
        _connection.ConnectionStatusChanged += (_, e) => captured = e;

        _bus.ConnectionStateChanged += Raise.Event<EventHandler<KnxConnEventArgs>>(null, new KnxConnEventArgs());

        Assert.That(captured, Is.Not.Null);
    }
}
