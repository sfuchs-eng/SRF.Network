using SRF.Network.Knx.IpRouting;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="KnxIpHeader"/>.
///
/// The KNX/IP header is a fixed 6-byte structure:
///   [0]   Header length (always 0x06)
///   [1]   Protocol version (always 0x10)
///   [2-3] Service type (big-endian ushort, e.g. 0x0530 for Routing Indication)
///   [4-5] Total packet length including this header (big-endian ushort)
/// </summary>
[TestFixture]
public class KnxIpHeaderTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] Encode(KnxIpHeader header)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        header.Encode(writer);
        writer.Flush();
        return ms.ToArray();
    }

    private static KnxIpHeader Decode(byte[] bytes, KnxIpRoutingPayloadProvider? provider = null)
    {
        var header = new KnxIpHeader();
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        var providers = provider != null
            ? (IEnumerable<SRF.Industrial.Packets.IPayloadObjectProvider>)[provider]
            : [];
        header.Decode(reader, providers);
        return header;
    }

    private static byte[] BuildRawHeader(ushort serviceType, ushort totalLength)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(KnxIpHeader.KnxIpHeaderLength);                // 0x06
        w.Write(KnxIpHeader.KnxIpProtocolVersion);             // 0x10
        w.Write((byte)(serviceType >> 8));                      // service type high (big-endian)
        w.Write((byte)(serviceType & 0xFF));                    // service type low
        w.Write((byte)(totalLength >> 8));                      // total length high (big-endian)
        w.Write((byte)(totalLength & 0xFF));                    // total length low
        w.Flush();
        return ms.ToArray();
    }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    [Test]
    public void Constants_HaveExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KnxIpHeader.KnxIpHeaderLength, Is.EqualTo(6));
            Assert.That(KnxIpHeader.KnxIpProtocolVersion, Is.EqualTo(0x10));
            Assert.That(KnxIpHeader.RoutingIndicationServiceType, Is.EqualTo(0x0530));
        });
    }

    // -------------------------------------------------------------------------
    // Measure() tests
    // -------------------------------------------------------------------------

    [Test]
    public void Measure_WithoutPayload_Returns6()
    {
        var header = new KnxIpHeader();
        Assert.That(header.Measure(), Is.EqualTo(6));
    }

    [Test]
    public void Measure_WithPayload_IncludesPayloadSize()
    {
        // Use a CemiLDataFrame as payload (ValueRead = 11 bytes)
        var payload = new CemiLDataFrame
        {
            EventType = SRF.Network.Knx.Messages.GroupEventType.ValueRead,
            Value = new SRF.Knx.Core.GroupValue([])
        };
        var header = new KnxIpHeader { Payload = payload };
        Assert.That(header.Measure(), Is.EqualTo(6 + payload.Measure()));
    }

    // -------------------------------------------------------------------------
    // Encode() tests
    // -------------------------------------------------------------------------

    [Test]
    public void Encode_HeaderLengthByte_IsAlways6()
    {
        var bytes = Encode(new KnxIpHeader());
        Assert.That(bytes[0], Is.EqualTo(0x06));
    }

    [Test]
    public void Encode_ProtocolVersionByte_IsAlways0x10()
    {
        var bytes = Encode(new KnxIpHeader());
        Assert.That(bytes[1], Is.EqualTo(0x10));
    }

    [Test]
    public void Encode_ServiceType_IsRoutingIndicationByDefault()
    {
        var bytes = Encode(new KnxIpHeader());
        ushort serviceType = (ushort)((bytes[2] << 8) | bytes[3]);
        Assert.That(serviceType, Is.EqualTo(KnxIpHeader.RoutingIndicationServiceType));
    }

    [Test]
    public void Encode_ServiceType_IsWrittenBigEndian()
    {
        var header = new KnxIpHeader { ServiceType = 0x0530 };
        var bytes = Encode(header);
        Assert.That(bytes[2], Is.EqualTo(0x05), "Service type high byte");
        Assert.That(bytes[3], Is.EqualTo(0x30), "Service type low byte");
    }

    [Test]
    public void Encode_TotalLength_IsHeaderPlusPayload()
    {
        // ValueRead CemiLDataFrame Measure() = 11 bytes (expected correct size)
        var payload = new CemiLDataFrame
        {
            EventType = SRF.Network.Knx.Messages.GroupEventType.ValueRead,
            Value = new SRF.Knx.Core.GroupValue([])
        };
        var header = new KnxIpHeader { Payload = payload };
        var bytes = Encode(header);
        ushort totalLength = (ushort)((bytes[4] << 8) | bytes[5]);
        Assert.That(totalLength, Is.EqualTo((ushort)header.Measure()));
    }

    [Test]
    public void Encode_WithoutPayload_TotalLengthIsExactly6()
    {
        var bytes = Encode(new KnxIpHeader());
        ushort totalLength = (ushort)((bytes[4] << 8) | bytes[5]);
        Assert.That(totalLength, Is.EqualTo(6));
    }

    [Test]
    public void Encode_ProducesExactly6BytesWithoutPayload()
    {
        Assert.That(Encode(new KnxIpHeader()), Has.Length.EqualTo(6));
    }

    // -------------------------------------------------------------------------
    // Decode() tests
    // -------------------------------------------------------------------------

    [Test]
    public void Decode_ValidRoutingIndicationHeader_RestoresServiceType()
    {
        var raw = BuildRawHeader(0x0530, 17);
        var header = Decode(raw);
        Assert.That(header.ServiceType, Is.EqualTo(0x0530));
    }

    [Test]
    public void Decode_WrongHeaderLength_ThrowsInvalidDataException()
    {
        // Build a header with length 0x05 instead of 0x06
        var raw = new byte[] { 0x05, 0x10, 0x05, 0x30, 0x00, 0x11 };
        Assert.Throws<InvalidDataException>(() => Decode(raw));
    }

    [Test]
    public void Decode_WrongProtocolVersion_ThrowsInvalidDataException()
    {
        var raw = new byte[] { 0x06, 0x20, 0x05, 0x30, 0x00, 0x11 }; // version 0x20 ≠ 0x10
        Assert.Throws<InvalidDataException>(() => Decode(raw));
    }

    [Test]
    public void Decode_WithRoutingPayloadProvider_AssignsCemiPayload()
    {
        // Build a full KNX/IP Routing Indication frame in memory
        var cemi = new CemiLDataFrame
        {
            EventType = SRF.Network.Knx.Messages.GroupEventType.ValueRead,
            Value = new SRF.Knx.Core.GroupValue([])
        };
        // Manually build the expected frame bytes (header + cEMI)
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)0x06);  // header len
        w.Write((byte)0x10);  // version
        w.Write((byte)0x05); w.Write((byte)0x30);  // service type = 0x0530 (big-endian)
        // Total length = 6 + 11 = 17 → 0x00 0x11 (using expected correct cEMI size)
        // We use the actual size to be independent of Measure() bugs
        using var cemiMs = new MemoryStream();
        using var cemiW = new BinaryWriter(cemiMs);
        cemi.Encode(cemiW); cemiW.Flush();
        byte[] cemiBytes = cemiMs.ToArray();
        int totalLength = 6 + cemiBytes.Length;
        w.Write((byte)(totalLength >> 8));
        w.Write((byte)(totalLength & 0xFF));
        foreach (var b in cemiBytes) w.Write(b);
        w.Flush();

        var header = Decode(ms.ToArray(), new KnxIpRoutingPayloadProvider());
        Assert.That(header.Payload, Is.InstanceOf<CemiLDataFrame>());
    }

    [Test]
    public void Decode_WithoutPayloadProvider_PayloadIsNull()
    {
        var raw = BuildRawHeader(0x0530, 6);
        var header = Decode(raw);
        Assert.That(header.Payload, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Round-trip tests
    // -------------------------------------------------------------------------

    [Test]
    public void RoundTrip_HeaderOnly_ServiceTypeIsPreserved()
    {
        var original = new KnxIpHeader { ServiceType = 0x0530 };
        var bytes = Encode(original);
        var decoded = Decode(bytes);
        Assert.That(decoded.ServiceType, Is.EqualTo(0x0530));
    }
}
