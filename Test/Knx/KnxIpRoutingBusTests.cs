using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Knx.Messages;
using SRF.Network.Test.Knx.TestHelpers;
using SRF.Network.Udp;
using KnxConnEventArgs = SRF.Network.Knx.KnxConnectionEventArgs;

namespace SRF.Network.Test.Knx;

[TestFixture]
public class KnxIpRoutingBusTests
{
    private IUdpMulticastClient _udpClient = null!;
    private IKnxIpRoutingQueue _sendQueue = null!;
    private FakeTimeProvider _timeProvider = null!;
    private KnxIpRoutingBus _bus = null!;
    private static readonly IPEndPoint DummyEndpoint = new(IPAddress.Parse("224.0.23.12"), 3671);

    private static byte[] BuildRoutingFrame(
        ushort srcAddr,
        ushort dstAddr,
        GroupEventType eventType,
        byte[] value,
        byte ctrl1 = 0xBC,
        byte ctrl2 = 0xE0)
    {
        byte serviceCode = eventType switch
        {
            GroupEventType.ValueRead     => 0x00,
            GroupEventType.ValueResponse => 0x40,
            GroupEventType.ValueWrite    => 0x80,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

        bool isSmallData = eventType != GroupEventType.ValueRead
                           && value.Length == 1
                           && (value[0] & 0xC0) == 0;

        byte apciLow;
        byte[] extraBytes;

        if (eventType == GroupEventType.ValueRead)
        {
            apciLow = 0x00;
            extraBytes = [];
        }
        else if (isSmallData)
        {
            apciLow = (byte)(serviceCode | (value[0] & 0x3F));
            extraBytes = [];
        }
        else
        {
            apciLow = serviceCode;
            extraBytes = value;
        }

        byte dataLength = (byte)(1 + extraBytes.Length);

        using var cemiMs = new MemoryStream();
        using var cemiW = new BinaryWriter(cemiMs);
        cemiW.Write((byte)CemiLDataFrame.MessageCodeInd);
        cemiW.Write((byte)0x00);
        cemiW.Write(ctrl1);
        cemiW.Write(ctrl2);
        cemiW.Write((byte)(srcAddr >> 8));
        cemiW.Write((byte)(srcAddr & 0xFF));
        cemiW.Write((byte)(dstAddr >> 8));
        cemiW.Write((byte)(dstAddr & 0xFF));
        cemiW.Write(dataLength);
        cemiW.Write((byte)0x00);
        cemiW.Write(apciLow);
        foreach (var b in extraBytes) cemiW.Write(b);
        cemiW.Flush();
        var cemiBytes = cemiMs.ToArray();

        int totalLength = 6 + cemiBytes.Length;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)0x06);
        w.Write((byte)0x10);
        w.Write((byte)0x05); w.Write((byte)0x30);
        w.Write((byte)(totalLength >> 8));
        w.Write((byte)(totalLength & 0xFF));
        foreach (var b in cemiBytes) w.Write(b);
        w.Flush();
        return ms.ToArray();
    }

    [SetUp]
    public async Task SetUp()
    {
        _udpClient = Substitute.For<IUdpMulticastClient>();
        _sendQueue = Substitute.For<IKnxIpRoutingQueue>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _udpClient.IsConnected.Returns(false);
        _udpClient.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _udpClient.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration { ConnectionString = "Type=IpRouting;KnxAddress=1.1.5" });
        var zeroRateOptions = Options.Create(new KnxIpRoutingOptions { BusBitRate = 0 });

        _bus = new KnxIpRoutingBus(
            _udpClient,
            _sendQueue,
            options,
            zeroRateOptions,
            NullLogger<KnxIpRoutingBus>.Instance,
            _timeProvider);

        await _bus.ConnectAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _udpClient.Dispose();
    }

    // ---- Constructor validation ----

    [Test]
    public void Constructor_NullUdpClient_ThrowsArgumentNullException()
    {
        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration());
        Assert.Throws<ArgumentNullException>(() =>
            new KnxIpRoutingBus(null!, _sendQueue, options, Options.Create(new KnxIpRoutingOptions()), NullLogger<KnxIpRoutingBus>.Instance, _timeProvider));
    }

    [Test]
    public void Constructor_NullUdpQueue_ThrowsArgumentNullException()
    {
        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration());
        Assert.Throws<ArgumentNullException>(() =>
            new KnxIpRoutingBus(_udpClient, null!, options, Options.Create(new KnxIpRoutingOptions()), NullLogger<KnxIpRoutingBus>.Instance, _timeProvider));
    }

    // ---- Connection state ----

    [Test]
    public void IsConnected_ReflectsUdpClientState()
    {
        _udpClient.IsConnected.Returns(true);
        Assert.That(_bus.IsConnected, Is.True);

        _udpClient.IsConnected.Returns(false);
        Assert.That(_bus.IsConnected, Is.False);
    }

    [Test]
    public void ConnectionState_Connected_WhenUdpClientIsConnected()
    {
        _udpClient.IsConnected.Returns(true);
        Assert.That(_bus.ConnectionState, Is.EqualTo(BusConnectionState.Connected));
    }

    [Test]
    public void ConnectionState_Closed_WhenUdpClientIsNotConnected()
    {
        _udpClient.IsConnected.Returns(false);
        Assert.That(_bus.ConnectionState, Is.EqualTo(BusConnectionState.Closed));
    }

    // ---- ConnectAsync / DisconnectAsync ----

    [Test]
    public async Task ConnectAsync_CallsUdpClientConnectAsync()
    {
        await _udpClient.Received().ConnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_CallsUdpClientDisconnectAsync()
    {
        await _bus.DisconnectAsync();
        await _udpClient.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_UnsubscribesFromUdpMessageReceived()
    {
        await _bus.DisconnectAsync();

        bool fired = false;
        _bus.MessageReceived += (_, _) => fired = true;

        var rawBytes = BuildRoutingFrame(
            srcAddr: new IndividualAddress("1.1.5").Address,
            dstAddr: new GroupAddress("0/0/1").Address,
            eventType: GroupEventType.ValueWrite,
            value: [0x01]);

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(fired, Is.False, "After disconnect, UDP messages should not be processed");
    }

    // ---- SendGroupMessageAsync ----

    [Test]
    public void SendGroupMessageAsync_NullMessage_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => _bus.SendGroupMessageAsync(null!));
    }

    [Test]
    public async Task SendGroupMessageAsync_EnqueuesBytesOnSendQueue()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]));
        await _bus.SendGroupMessageAsync(msg);
        _sendQueue.Received(1).Enqueue(Arg.Any<byte[]>(), Arg.Any<int>());
    }

    [Test]
    public async Task SendGroupMessageAsync_EnqueuedBytes_StartWithKnxIpHeader()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]));
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(enqueuedBytes![0], Is.EqualTo(0x06));
            Assert.That(enqueuedBytes[1], Is.EqualTo(0x10));
            Assert.That(enqueuedBytes[2], Is.EqualTo(0x05));
            Assert.That(enqueuedBytes[3], Is.EqualTo(0x30));
        });
    }

    [Test]
    public async Task SendGroupMessageAsync_DestinationAddress_IsEncodedInCemiFrame()
    {
        var dest = new GroupAddress("5/6/7");
        var msg = GroupMessageRequest.Write(dest, new GroupValue([0x01]));
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes, Is.Not.Null);
        ushort destEncoded = (ushort)((enqueuedBytes![12] << 8) | enqueuedBytes[13]);
        Assert.That(destEncoded, Is.EqualTo(dest.Address));
    }

    [Test]
    public async Task SendGroupMessageAsync_SourceAddress_ParsedFromConnectionString()
    {
        var expectedSrc = new IndividualAddress("1.1.5");
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]));
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes, Is.Not.Null);
        ushort srcEncoded = (ushort)((enqueuedBytes![10] << 8) | enqueuedBytes[11]);
        Assert.That(srcEncoded, Is.EqualTo(expectedSrc.Address));
    }

    [Test]
    public async Task SendGroupMessageAsync_PriorityLow_EncodesCorrectCtrl1Bits()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]), MessagePriority.Low);
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes![8] & 0x0C, Is.EqualTo((int)MessagePriority.Low << 2));
    }

    [Test]
    public async Task SendGroupMessageAsync_PriorityAlarm_EncodesCorrectCtrl1Bits()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]), MessagePriority.Alarm);
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes![8] & 0x0C, Is.EqualTo((int)MessagePriority.Alarm << 2));
    }

    // ---- Receive path — valid frames ----

    [Test]
    public void ReceivePath_ValueWrite_SmallData_FiresMessageReceived()
    {
        var src = new IndividualAddress("1.1.5");
        var dst = new GroupAddress("5/6/7");
        var value = new byte[] { 0x01 };
        var rawBytes = BuildRoutingFrame(src.Address, dst.Address, GroupEventType.ValueWrite, value);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.KnxMessageContext.GroupEventArgs!.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.DestinationAddress.Address, Is.EqualTo(dst.Address));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.SourceAddress.Address, Is.EqualTo(src.Address));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.Value.Value, Is.EqualTo(value));
        });
    }

    [Test]
    public void ReceivePath_ValueRead_FiresMessageReceived_WithEmptyValue()
    {
        var rawBytes = BuildRoutingFrame(
            new IndividualAddress("1.1.5").Address,
            new GroupAddress("0/0/1").Address,
            GroupEventType.ValueRead, []);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.KnxMessageContext.GroupEventArgs!.EventType, Is.EqualTo(GroupEventType.ValueRead));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.Value.Value, Is.Empty);
        });
    }

    [Test]
    public void ReceivePath_ValueResponse_LargeData_FiresMessageReceived()
    {
        var src = new IndividualAddress("1.1.5");
        var dst = new GroupAddress("1/0/0");
        var value = new byte[] { 0x04, 0x1A };
        var rawBytes = BuildRoutingFrame(src.Address, dst.Address, GroupEventType.ValueResponse, value);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.KnxMessageContext.GroupEventArgs!.EventType, Is.EqualTo(GroupEventType.ValueResponse));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.Value.Value, Is.EqualTo(value));
        });
    }

    [Test]
    public void ReceivePath_UsesTimeProviderTimestamp()
    {
        var fixedTime = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        _timeProvider.Advance(fixedTime - _timeProvider.GetUtcNow());

        var rawBytes = BuildRoutingFrame(0x0001, 0x0001, GroupEventType.ValueRead, []);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(captured!.KnxMessageContext.ReceivedAt, Is.EqualTo(fixedTime));
    }

    [Test]
    public void ReceivePath_PriorityLow_DecodedInGroupEventArgs()
    {
        byte ctrl1 = (byte)((0xBC & ~0x0C) | ((int)MessagePriority.Low << 2));
        var rawBytes = BuildRoutingFrame(0x0001, 0x0001, GroupEventType.ValueWrite, [0x01], ctrl1);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(captured!.KnxMessageContext.GroupEventArgs!.Priority, Is.EqualTo(MessagePriority.Low));
    }

    [Test]
    public void ReceivePath_PriorityAlarm_DecodedInGroupEventArgs()
    {
        byte ctrl1 = (byte)((0xBC & ~0x0C) | ((int)MessagePriority.Alarm << 2));
        var rawBytes = BuildRoutingFrame(0x0001, 0x0001, GroupEventType.ValueWrite, [0x01], ctrl1);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(captured!.KnxMessageContext.GroupEventArgs!.Priority, Is.EqualTo(MessagePriority.Alarm));
    }

    // ---- Receive path — invalid/unsupported frames ----

    [Test]
    public void ReceivePath_UnsupportedServiceType_SilentlyIgnored()
    {
        var bytes = new byte[] { 0x06, 0x10, 0x02, 0x01, 0x00, 0x06 };

        bool fired = false;
        _bus.MessageReceived += (_, _) => fired = true;

        Assert.DoesNotThrow(() =>
            _udpClient.MessageReceived += Raise.EventWith(
                new UdpMessageReceivedEventArgs(bytes, DummyEndpoint, DateTimeOffset.UtcNow)));

        Assert.That(fired, Is.False);
    }

    [Test]
    public void ReceivePath_MalformedBytes_DoesNotThrow_AndDoesNotFireEvent()
    {
        var garbage = new byte[] { 0xFF, 0xFF, 0x00, 0x00 };

        bool fired = false;
        _bus.MessageReceived += (_, _) => fired = true;

        Assert.DoesNotThrow(() =>
            _udpClient.MessageReceived += Raise.EventWith(
                new UdpMessageReceivedEventArgs(garbage, DummyEndpoint, DateTimeOffset.UtcNow)));

        Assert.That(fired, Is.False);
    }

    [Test]
    public void ReceivePath_EmptyBytes_DoesNotThrow()
    {
        bool fired = false;
        _bus.MessageReceived += (_, _) => fired = true;

        Assert.DoesNotThrow(() =>
            _udpClient.MessageReceived += Raise.EventWith(
                new UdpMessageReceivedEventArgs([], DummyEndpoint, DateTimeOffset.UtcNow)));

        Assert.That(fired, Is.False);
    }

    // ---- ParseKnxAddress fallback ----

    [Test]
    public async Task ParseKnxAddress_MissingKnxAddressKey_UsesFallback()
    {
        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration { ConnectionString = "Type=IpRouting" });
        var busNoAddress = new KnxIpRoutingBus(
            _udpClient, _sendQueue, options,
            Options.Create(new KnxIpRoutingOptions { BusBitRate = 0 }),
            NullLogger<KnxIpRoutingBus>.Instance, _timeProvider);
        await busNoAddress.ConnectAsync();

        await busNoAddress.SendGroupMessageAsync(
            GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01])));

        _sendQueue.Received(1).Enqueue(Arg.Any<byte[]>(), Arg.Any<int>());
        // Just confirm it enqueued something without throwing
    }

    // ---- UDP connection status forwarding ----

    [Test]
    public void UdpConnectionStatusChanged_ForwardedAsKnxConnectionStateChanged()
    {
        KnxConnEventArgs? captured = null;
        _bus.ConnectionStateChanged += (_, e) => captured = e;

        _udpClient.ConnectionStatusChanged += Raise.EventWith(new UdpConnectionEventArgs(true));

        Assert.That(captured, Is.Not.Null);
    }

    // ---- Priority coverage: Normal and System ----

    [Test]
    public async Task SendGroupMessageAsync_PriorityNormal_EncodesCorrectCtrl1Bits()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]), MessagePriority.Normal);
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes![8] & 0x0C, Is.EqualTo((int)MessagePriority.Normal << 2));
    }

    [Test]
    public async Task SendGroupMessageAsync_PrioritySystem_EncodesCorrectCtrl1Bits()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([0x01]), MessagePriority.System);
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        Assert.That(enqueuedBytes![8] & 0x0C, Is.EqualTo((int)MessagePriority.System << 2));
    }

    [Test]
    public void ReceivePath_PriorityNormal_DecodedInGroupEventArgs()
    {
        byte ctrl1 = (byte)((0xBC & ~0x0C) | ((int)MessagePriority.Normal << 2));
        var rawBytes = BuildRoutingFrame(0x0001, 0x0001, GroupEventType.ValueWrite, [0x01], ctrl1);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(captured!.KnxMessageContext.GroupEventArgs!.Priority, Is.EqualTo(MessagePriority.Normal));
    }

    [Test]
    public void ReceivePath_PrioritySystem_DecodedInGroupEventArgs()
    {
        byte ctrl1 = (byte)((0xBC & ~0x0C) | ((int)MessagePriority.System << 2));
        var rawBytes = BuildRoutingFrame(0x0001, 0x0001, GroupEventType.ValueWrite, [0x01], ctrl1);

        KnxMessageReceivedEventArgs? captured = null;
        _bus.MessageReceived += (_, e) => captured = e;

        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        Assert.That(captured!.KnxMessageContext.GroupEventArgs!.Priority, Is.EqualTo(MessagePriority.System));
    }

    // ---- High-load ----

    [Test]
    public async Task SendGroupMessageAsync_MultipleRapidSends_AllEnqueued()
    {
        const int count = 10;
        var tasks = Enumerable.Range(0, count)
            .Select(i => _bus.SendGroupMessageAsync(
                GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([(byte)(i & 0x3F)]))));

        await Task.WhenAll(tasks);

        _sendQueue.Received(count).Enqueue(Arg.Any<byte[]>(), Arg.Any<int>());
    }

    // ---- Large data encoding ----

    [Test]
    public async Task SendGroupMessageAsync_FourByteValue_EncodedAsSeparateDataBytes()
    {
        var value = new byte[] { 0x00, 0x00, 0x01, 0xF4 };
        var msg = GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue(value));
        byte[]? enqueuedBytes = null;
        _sendQueue.When(q => q.Enqueue(Arg.Any<byte[]>(), Arg.Any<int>()))
                  .Do(ci => enqueuedBytes = (byte[])ci[0]);

        await _bus.SendGroupMessageAsync(msg);

        // KNX/IP header (6) + cEMI fixed (11) + 4 data bytes = 21 bytes total
        Assert.That(enqueuedBytes, Is.Not.Null);
        Assert.That(enqueuedBytes!.Length, Is.EqualTo(21));
        // Data bytes follow after the APCI low byte (index 16): indices 17..20
        Assert.That(enqueuedBytes[17..21], Is.EqualTo(value));
    }

    // ---- Receive path notifies queue ----

    [Test]
    public void ReceivePath_UdpMessageReceived_CallsNotifyReceivedOnQueue()
    {
        var rawBytes = BuildRoutingFrame(
            srcAddr: new IndividualAddress("2.1.1").Address,
            dstAddr: new GroupAddress("0/0/1").Address,
            eventType: GroupEventType.ValueWrite,
            value: [0x01]);
        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(rawBytes, DummyEndpoint, DateTimeOffset.UtcNow));

        _sendQueue.Received(1).NotifyReceived(Arg.Any<int>());
    }
}
