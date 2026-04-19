using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Knx.Messages;
using SRF.Network.Test.Knx.TestHelpers;
using SRF.Network.Udp;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Integration tests for the full KNX stack:
/// <see cref="KnxConnection"/> → <see cref="KnxIpRoutingBus"/> → mock UDP transport.
/// Verifies end-to-end send/receive functionality, DPT decoding, and connection lifecycle
/// using real production instances of both <see cref="KnxConnection"/> and
/// <see cref="KnxIpRoutingBus"/> with mocked UDP transport.
/// </summary>
[TestFixture]
public class KnxConnectionIntegrationTests
{
    private IUdpMulticastClient _udpClient = null!;
    private IUdpMessageQueue _udpQueue = null!;
    private IDptResolver _dptResolver = null!;
    private KnxConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;

    private static readonly IPEndPoint DummyEndpoint = new(IPAddress.Parse("224.0.23.12"), 3671);
    private static readonly IndividualAddress DefaultSource = new("1.1.5");

    // ---- Test helpers ----

    private sealed class StubDpt : DptBase
    {
        private readonly object _returnValue;

        [SetsRequiredMembers]
        public StubDpt(object returnValue, int dptMain = 1, int dptSub = 1)
        {
            _returnValue = returnValue;
            Id = new DataPointTypeId(dptMain, dptSub);
        }

        public override object ToValue(GroupValue groupValue) => _returnValue;
        public override GroupValue ToGroupValue(object value) => new([]);
    }

    /// <summary>
    /// Builds a valid KNX/IP Routing Indication frame using the production encoding classes.
    /// This ensures integration tests exercise the same wire format as production code.
    /// </summary>
    private static byte[] BuildFrame(
        GroupEventType eventType,
        GroupAddress dst,
        IndividualAddress src,
        byte[] value,
        byte ctrl1 = 0xBC)
    {
        var cemi = new CemiLDataFrame
        {
            MessageCode        = CemiLDataFrame.MessageCodeInd,
            Ctrl1              = ctrl1,
            SourceAddress      = src,
            DestinationAddress = dst,
            EventType          = eventType,
            Value              = new GroupValue(value),
        };
        var header = new KnxIpHeader { Payload = cemi };
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        header.Encode(w);
        return ms.ToArray();
    }

    private void SimulateUdpReceive(byte[] data) =>
        _udpClient.MessageReceived += Raise.EventWith(
            new UdpMessageReceivedEventArgs(data, DummyEndpoint, DateTimeOffset.UtcNow));

    [SetUp]
    public async Task SetUp()
    {
        _udpClient   = Substitute.For<IUdpMulticastClient>();
        _udpQueue    = Substitute.For<IUdpMessageQueue>();
        _dptResolver = Substitute.For<IDptResolver>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        // Track connection state so KnxConnection.ConnectAsync sees the right state
        // after KnxIpRoutingBus.ConnectAsync completes.
        bool connected = false;
        _udpClient.IsConnected.Returns(_ => connected);
        _udpClient.ConnectAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { connected = true; return Task.CompletedTask; });
        _udpClient.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { connected = false; return Task.CompletedTask; });
        _udpQueue.Enqueue(Arg.Any<byte[]>())
            .Returns(ci => new UdpQueueItem((byte[])ci[0], DateTimeOffset.UtcNow));

        var options = Substitute.For<IOptions<KnxConfiguration>>();
        options.Value.Returns(new KnxConfiguration { ConnectionString = "Type=IpRouting;KnxAddress=1.1.5" });

        var bus = new KnxIpRoutingBus(
            _udpClient, _udpQueue, options,
            Options.Create(new KnxIpRoutingOptions { AverageTelegramBits = 0 }),
            NullLogger<KnxIpRoutingBus>.Instance, _timeProvider);

        _connection = new KnxConnection(
            Substitute.For<IKnxLibraryInitialization>(),
            bus,
            options,
            NullLogger<KnxConnection>.Instance,
            _dptResolver);

        await _connection.ConnectAsync();
    }

    [TearDown]
    public void TearDown() => _udpClient.Dispose();

    // -------------------------------------------------------------------------
    // Send path: KnxConnection.SendMessageAsync → KnxIpRoutingBus → IUdpMessageQueue
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendMessage_FullStack_EncodedBytesReachUdpQueue()
    {
        var msg = GroupMessageRequest.Write(new GroupAddress("5/1/0"), new GroupValue([0x01]));
        byte[]? enqueuedBytes = null;
        _udpQueue.Enqueue(Arg.Do<byte[]>(b => enqueuedBytes = b))
            .Returns(ci => new UdpQueueItem((byte[])ci[0], DateTimeOffset.UtcNow));

        await _connection.SendMessageAsync(msg, CancellationToken.None);

        Assert.That(enqueuedBytes, Is.Not.Null, "Bytes must reach the UDP queue through the full stack");
        Assert.Multiple(() =>
        {
            Assert.That(enqueuedBytes![0], Is.EqualTo(0x06), "KNX/IP: protocol identifier");
            Assert.That(enqueuedBytes[1], Is.EqualTo(0x10), "KNX/IP: protocol version 1.0");
            Assert.That(enqueuedBytes[2], Is.EqualTo(0x05), "KNX/IP: service type 0x0530 high byte");
            Assert.That(enqueuedBytes[3], Is.EqualTo(0x30), "KNX/IP: service type 0x0530 low byte");
        });
    }

    [Test]
    public async Task SendMessage_FullStack_DestinationAddressEncodedInFrame()
    {
        var dest = new GroupAddress("3/2/1");
        var msg = GroupMessageRequest.Write(dest, new GroupValue([0x01]));
        byte[]? enqueuedBytes = null;
        _udpQueue.Enqueue(Arg.Do<byte[]>(b => enqueuedBytes = b))
            .Returns(ci => new UdpQueueItem((byte[])ci[0], DateTimeOffset.UtcNow));

        await _connection.SendMessageAsync(msg, CancellationToken.None);

        Assert.That(enqueuedBytes, Is.Not.Null);
        // KNX/IP header (6) + cEMI: msgCode(1)+addInfo(1)+ctrl1(1)+ctrl2(1)+src(2) = 12 bytes before dest
        ushort destEncoded = (ushort)((enqueuedBytes![12] << 8) | enqueuedBytes[13]);
        Assert.That(destEncoded, Is.EqualTo(dest.Address));
    }

    // -------------------------------------------------------------------------
    // Receive path: UDP event → KnxIpRoutingBus → KnxConnection.MessageReceived
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveMessage_FullStack_KnxConnectionFiresMessageReceived()
    {
        var src = new IndividualAddress("2.1.3");
        var dst = new GroupAddress("0/0/1");
        var value = new byte[] { 0x1F };

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        SimulateUdpReceive(BuildFrame(GroupEventType.ValueWrite, dst, src, value));

        Assert.That(captured, Is.Not.Null, "KnxConnection.MessageReceived must fire for valid incoming frames");
        Assert.Multiple(() =>
        {
            Assert.That(captured!.KnxMessageContext.GroupEventArgs!.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.DestinationAddress.Address, Is.EqualTo(dst.Address));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.SourceAddress.Address, Is.EqualTo(src.Address));
            Assert.That(captured.KnxMessageContext.GroupEventArgs.Value.Value, Is.EqualTo(value));
        });
    }

    [Test]
    public void ReceiveMessage_FullStack_LargeValue_PreservedThroughStack()
    {
        var dst   = new GroupAddress("1/0/5");
        var value = new byte[] { 0x00, 0x00, 0x01, 0xF4 }; // 4-byte value e.g. DPT-9

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        SimulateUdpReceive(BuildFrame(GroupEventType.ValueResponse, dst, DefaultSource, value));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.KnxMessageContext.GroupEventArgs!.Value.Value, Is.EqualTo(value));
    }

    [Test]
    public void ReceiveMessage_FullStack_DptValueIsDecoded()
    {
        var dst = new GroupAddress("0/0/1");
        const int expectedDecodedValue = 42;
        var dpt = new StubDpt(returnValue: expectedDecodedValue);
        _dptResolver.GetDpt(Arg.Any<GroupAddress>()).Returns(dpt);

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        SimulateUdpReceive(BuildFrame(GroupEventType.ValueResponse, dst, DefaultSource, [0x2A]));

        Assert.That(captured, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(captured!.KnxMessageContext.Dpt, Is.SameAs(dpt), "Resolved DPT must be stored in context");
            Assert.That(captured.KnxMessageContext.DecodedValue, Is.EqualTo(expectedDecodedValue), "DPT-decoded value must be stored in context");
        });
    }

    [Test]
    public void ReceiveMessage_FullStack_DptResolutionFailure_MessageStillForwarded()
    {
        var dst = new GroupAddress("0/0/2");
        _dptResolver.GetDpt(Arg.Any<GroupAddress>())
            .Returns(_ => throw new KnxException("No DPT configured for address"));

        KnxMessageReceivedEventArgs? captured = null;
        _connection.MessageReceived += (_, e) => captured = e;

        SimulateUdpReceive(BuildFrame(GroupEventType.ValueWrite, dst, DefaultSource, [0x01]));

        Assert.That(captured, Is.Not.Null, "Message must still be forwarded when DPT resolution fails");
        Assert.That(captured!.KnxMessageContext.DecodedValue, Is.Null, "DecodedValue must be null when DPT resolution fails");
    }

    [Test]
    public void ReceiveMessage_MalformedUdpBytes_DoesNotFireEvent()
    {
        bool fired = false;
        _connection.MessageReceived += (_, _) => fired = true;

        SimulateUdpReceive([0xFF, 0x00, 0xAB]);

        Assert.That(fired, Is.False, "Malformed frames must not fire KnxConnection.MessageReceived");
    }

    // -------------------------------------------------------------------------
    // Connection lifecycle
    // -------------------------------------------------------------------------

    [Test]
    public async Task ConnectionLifecycle_AfterDisconnect_ReceivedFramesAreNotForwarded()
    {
        bool fired = false;
        _connection.MessageReceived += (_, _) => fired = true;

        await _connection.DisconnectAsync();

        SimulateUdpReceive(BuildFrame(GroupEventType.ValueWrite, new GroupAddress("0/0/1"), DefaultSource, [0x01]));

        Assert.That(fired, Is.False, "After disconnect, incoming frames must not reach KnxConnection.MessageReceived");
    }

    [Test]
    public async Task ConnectionLifecycle_IsConnected_ReflectsBusState()
    {
        // After SetUp ConnectAsync, bus is connected
        Assert.That(_connection.IsConnected, Is.True);

        await _connection.DisconnectAsync();
        Assert.That(_connection.IsConnected, Is.False);
    }

    // -------------------------------------------------------------------------
    // High-load: multiple rapid messages
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendMessage_MultipleRapidMessages_AllEnqueued()
    {
        const int count = 20;
        var tasks = Enumerable.Range(0, count)
            .Select(i => _connection.SendMessageAsync(
                GroupMessageRequest.Write(new GroupAddress("0/0/1"), new GroupValue([(byte)(i & 0x3F)])),
                CancellationToken.None));

        await Task.WhenAll(tasks);

        _udpQueue.Received(count).Enqueue(Arg.Any<byte[]>());
    }
}
