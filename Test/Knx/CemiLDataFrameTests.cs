using SRF.Knx.Core;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="CemiLDataFrame"/> covering frame measurement,
/// encoding (KNX wire format), decoding, and encode/decode round-trips.
///
/// A cEMI L_DATA frame for KNX/IP Routing has this wire layout (bytes 0–N):
///   [0]  Message code (0x29 = L_DATA.ind, 0x11 = L_DATA.req)
///   [1]  Additional info length (0x00)
///   [2]  Ctrl1
///   [3]  Ctrl2 (bit 7 = group dest, bits 6-4 = hop count)
///   [4-5] Source individual address (big-endian)
///   [6-7] Destination group address (big-endian)
///   [8]  Data length = APDU byte count after TPCI byte
///   [9]  TPCI (0x00 for group data)
///   [10] APCI low byte — bits [7:6] carry the service type:
///          0b00 = GroupValueRead (0x00)
///          0b01 = GroupValueResponse (0x40)
///          0b10 = GroupValueWrite (0x80)
///        bits [5:0] carry small data (≤ 6 bits)
///   [11+] Extra data bytes for payloads that do not fit in 6 bits
/// </summary>
[TestFixture]
public class CemiLDataFrameTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] Encode(CemiLDataFrame frame)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        frame.Encode(writer);
        writer.Flush();
        return ms.ToArray();
    }

    private static CemiLDataFrame Decode(byte[] bytes)
    {
        var frame = new CemiLDataFrame();
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        frame.Decode(reader, []);
        return frame;
    }

    /// <summary>
    /// Assembles a correct KNX/IP cEMI L_DATA.ind frame for use in Decode tests.
    /// This bypasses the Encode() method so decode tests are independent of encode bugs.
    /// </summary>
    private static byte[] BuildRawFrame(
        byte messageCode,
        byte ctrl1,
        byte ctrl2,
        ushort sourceAddr,
        ushort destAddr,
        byte apciLowByte,   // full APCI low byte including service type bits
        byte[]? extraData = null)
    {
        extraData ??= [];
        // dataLength = 1 (APCI low byte) + extra data bytes
        byte dataLength = (byte)(1 + extraData.Length);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(messageCode);
        w.Write((byte)0x00);                              // additional info len
        w.Write(ctrl1);
        w.Write(ctrl2);
        w.Write((byte)(sourceAddr >> 8));                 // source high (big-endian)
        w.Write((byte)(sourceAddr & 0xFF));               // source low
        w.Write((byte)(destAddr >> 8));                   // dest high (big-endian)
        w.Write((byte)(destAddr & 0xFF));                 // dest low
        w.Write(dataLength);
        w.Write((byte)0x00);                              // TPCI
        w.Write(apciLowByte);
        foreach (var b in extraData)
            w.Write(b);
        w.Flush();
        return ms.ToArray();
    }

    // -------------------------------------------------------------------------
    // Measure() tests
    // -------------------------------------------------------------------------

    [Test]
    public void Measure_ValueRead_Returns11Bytes()
    {
        // GroupValueRead has no data; the frame still needs the APCI low byte.
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueRead,
            Value = new GroupValue([])
        };
        Assert.That(frame.Measure(), Is.EqualTo(11),
            "ValueRead frame: 10 fixed bytes (msgCode…TPCI) + 1 APCI low byte = 11");
    }

    [Test]
    public void Measure_SmallData_Returns11Bytes()
    {
        // Small data (1 byte, ≤ 6 bits) is packed into the APCI low byte — no extra bytes.
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01])
        };
        Assert.That(frame.Measure(), Is.EqualTo(11),
            "Small-data frame: 10 fixed bytes + 1 APCI low byte = 11");
    }

    [Test]
    public void Measure_SingleByteExceedingSixBits_Returns12Bytes()
    {
        // A 1-byte value with high bits set cannot fit in 6-bit APCI slot → extra byte needed.
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x80])
        };
        Assert.That(frame.Measure(), Is.EqualTo(12),
            "1-byte large-data frame: 10 fixed + 1 APCI low + 1 extra = 12");
    }

    [Test]
    public void Measure_TwoByteData_Returns13Bytes()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01, 0x02])
        };
        Assert.That(frame.Measure(), Is.EqualTo(13),
            "2-byte data frame: 10 fixed + 1 APCI low + 2 extra = 13");
    }

    [Test]
    public void Measure_FourByteData_Returns15Bytes()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x00, 0x00, 0x12, 0x34])
        };
        Assert.That(frame.Measure(), Is.EqualTo(15),
            "4-byte data frame: 10 fixed + 1 APCI low + 4 extra = 15");
    }

    // -------------------------------------------------------------------------
    // Encode() tests — verify KNX wire format compliance
    // -------------------------------------------------------------------------

    [Test]
    public void Encode_MessageCode_IsWrittenAtByte0()
    {
        var frame = new CemiLDataFrame { MessageCode = CemiLDataFrame.MessageCodeInd };
        var bytes = Encode(frame);
        Assert.That(bytes[0], Is.EqualTo(CemiLDataFrame.MessageCodeInd));
    }

    [Test]
    public void Encode_Ctrl1_IsWrittenAtByte2()
    {
        const byte ctrl1 = 0xAC;
        var frame = new CemiLDataFrame { Ctrl1 = ctrl1 };
        var bytes = Encode(frame);
        Assert.That(bytes[2], Is.EqualTo(ctrl1));
    }

    [Test]
    public void Encode_Ctrl2_IsWrittenAtByte3()
    {
        const byte ctrl2 = 0xF0;
        var frame = new CemiLDataFrame { Ctrl2 = ctrl2 };
        var bytes = Encode(frame);
        Assert.That(bytes[3], Is.EqualTo(ctrl2));
    }

    [Test]
    public void Encode_SourceAddress_IsBigEndianAtBytes4And5()
    {
        // IndividualAddress "1.1.1" → area=1<<12, line=1<<8, device=1 → 0x1101
        var frame = new CemiLDataFrame
        {
            SourceAddress = new IndividualAddress("1.1.1")
        };
        var bytes = Encode(frame);
        ushort encoded = (ushort)((bytes[4] << 8) | bytes[5]);
        Assert.That(encoded, Is.EqualTo(new IndividualAddress("1.1.1").Address));
    }

    [Test]
    public void Encode_DestinationAddress_IsBigEndianAtBytes6And7()
    {
        // GroupAddress "1/2/3" → main=1<<11, middle=2<<8, sub=3 → 0x0A03
        var frame = new CemiLDataFrame
        {
            DestinationAddress = new GroupAddress("1/2/3")
        };
        var bytes = Encode(frame);
        ushort encoded = (ushort)((bytes[6] << 8) | bytes[7]);
        Assert.That(encoded, Is.EqualTo(new GroupAddress("1/2/3").Address));
    }

    [Test]
    public void Encode_ValueRead_ApciLowByte_HasReadServiceBits()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueRead,
            Value = new GroupValue([])
        };
        var bytes = Encode(frame);
        // Service bits [7:6] of APCI low byte must be 0b00 = 0x00 for GroupValueRead
        Assert.That(bytes[10] & 0xC0, Is.EqualTo(0x00),
            "GroupValueRead: APCI service bits [7:6] must be 0b00");
    }

    [Test]
    public void Encode_ValueResponse_ApciLowByte_HasResponseServiceBits()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueResponse,
            Value = new GroupValue([0x05])
        };
        var bytes = Encode(frame);
        // Service bits [7:6] of APCI low byte must be 0b01 = 0x40 for GroupValueResponse
        Assert.That(bytes[10] & 0xC0, Is.EqualTo(0x40),
            "GroupValueResponse: APCI service bits [7:6] must be 0b01 (0x40)");
    }

    [Test]
    public void Encode_ValueWrite_ApciLowByte_HasWriteServiceBits()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01])
        };
        var bytes = Encode(frame);
        // Service bits [7:6] of APCI low byte must be 0b10 = 0x80 for GroupValueWrite
        Assert.That(bytes[10] & 0xC0, Is.EqualTo(0x80),
            "GroupValueWrite: APCI service bits [7:6] must be 0b10 (0x80)");
    }

    [Test]
    public void Encode_SmallData_EmbeddedInApciLowByte()
    {
        // Small data (≤ 6 bits) travels in the low 6 bits of the APCI byte
        const byte value = 0x1F; // fits in 6 bits
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([value])
        };
        var bytes = Encode(frame);
        Assert.That(bytes[10] & 0x3F, Is.EqualTo(value),
            "Small data must appear in bits [5:0] of the APCI low byte");
        Assert.That(bytes, Has.Length.EqualTo(11),
            "Small-data frame must not have extra bytes beyond the APCI low byte");
    }

    [Test]
    public void Encode_LargeData_ApciLowByteContainsOnlyServiceBits_DataInExtraBytes()
    {
        // Large data (>6 bits) goes into extra bytes; APCI low contains only the service code
        var data = new byte[] { 0x01, 0xFF };
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue(data)
        };
        var bytes = Encode(frame);
        Assert.That(bytes[10] & 0x3F, Is.EqualTo(0x00),
            "Large-data APCI low byte must have 0 in the data bits");
        Assert.That(bytes[11], Is.EqualTo(0x01), "First extra data byte");
        Assert.That(bytes[12], Is.EqualTo(0xFF), "Second extra data byte");
        Assert.That(bytes, Has.Length.EqualTo(13));
    }

    [Test]
    public void Encode_ValueRead_DataLengthFieldIsOne()
    {
        // DataLength field = APDU bytes after TPCI = 1 (the APCI low byte only)
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueRead,
            Value = new GroupValue([])
        };
        var bytes = Encode(frame);
        Assert.That(bytes[8], Is.EqualTo(1), "DataLength for ValueRead should be 1 (APCI byte only)");
    }

    [Test]
    public void Encode_SmallData_DataLengthFieldIsOne()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01])
        };
        var bytes = Encode(frame);
        Assert.That(bytes[8], Is.EqualTo(1), "DataLength for small-data should be 1");
    }

    [Test]
    public void Encode_TwoByteData_DataLengthFieldIsThree()
    {
        var frame = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01, 0x02])
        };
        var bytes = Encode(frame);
        Assert.That(bytes[8], Is.EqualTo(3), "DataLength for 2-byte data: 1 (APCI) + 2 = 3");
    }

    // -------------------------------------------------------------------------
    // Decode() tests — decode correctly-formed KNX wire frames
    // -------------------------------------------------------------------------

    [Test]
    public void Decode_ValueRead_RestoresEventTypeAndEmptyValue()
    {
        var raw = BuildRawFrame(
            messageCode: CemiLDataFrame.MessageCodeInd,
            ctrl1: 0xBC, ctrl2: 0xE0,
            sourceAddr: new IndividualAddress("1.1.5").Address,
            destAddr: new GroupAddress("0/0/1").Address,
            apciLowByte: 0x00);  // GroupValueRead service bits = 0b00

        var frame = Decode(raw);

        Assert.Multiple(() =>
        {
            Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueRead));
            Assert.That(frame.Value.Value, Is.Empty);
            Assert.That(frame.MessageCode, Is.EqualTo(CemiLDataFrame.MessageCodeInd));
        });
    }

    [Test]
    public void Decode_ValueResponse_SmallData_RestoresEventTypeAndValue()
    {
        // GroupValueResponse service bits = 0b01 = 0x40; value 0x05 in bits [5:0]
        var raw = BuildRawFrame(
            messageCode: CemiLDataFrame.MessageCodeInd,
            ctrl1: 0xBC, ctrl2: 0xE0,
            sourceAddr: new IndividualAddress("1.1.5").Address,
            destAddr: new GroupAddress("0/0/1").Address,
            apciLowByte: 0x45);  // 0x40 (Response) | 0x05 (value)

        var frame = Decode(raw);

        Assert.Multiple(() =>
        {
            Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueResponse));
            Assert.That(frame.Value.Value, Is.EqualTo(new byte[] { 0x05 }));
        });
    }

    [Test]
    public void Decode_ValueWrite_SmallData_RestoresEventTypeAndValue()
    {
        // GroupValueWrite service bits = 0b10 = 0x80; value 0x01 in bits [5:0]
        var raw = BuildRawFrame(
            messageCode: CemiLDataFrame.MessageCodeInd,
            ctrl1: 0xBC, ctrl2: 0xE0,
            sourceAddr: new IndividualAddress("1.1.5").Address,
            destAddr: new GroupAddress("0/0/1").Address,
            apciLowByte: 0x81);  // 0x80 (Write) | 0x01 (value)

        var frame = Decode(raw);

        Assert.Multiple(() =>
        {
            Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(frame.Value.Value, Is.EqualTo(new byte[] { 0x01 }));
        });
    }

    [Test]
    public void Decode_ValueWrite_LargeData_RestoresEventTypeAndValue()
    {
        // Large 2-byte payload; service bits 0x80 in APCI low, data in extra bytes
        var raw = BuildRawFrame(
            messageCode: CemiLDataFrame.MessageCodeInd,
            ctrl1: 0xBC, ctrl2: 0xE0,
            sourceAddr: new IndividualAddress("1.1.5").Address,
            destAddr: new GroupAddress("1/2/100").Address,
            apciLowByte: 0x80,
            extraData: [0x0A, 0xBC]);

        var frame = Decode(raw);

        Assert.Multiple(() =>
        {
            Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(frame.Value.Value, Is.EqualTo(new byte[] { 0x0A, 0xBC }));
        });
    }

    [Test]
    public void Decode_SourceAndDestinationAddresses_AreParsedCorrectly()
    {
        var src = new IndividualAddress("2.3.45");
        var dst = new GroupAddress("4/5/200");

        var raw = BuildRawFrame(
            messageCode: CemiLDataFrame.MessageCodeInd,
            ctrl1: 0xBC, ctrl2: 0xE0,
            sourceAddr: src.Address,
            destAddr: dst.Address,
            apciLowByte: 0x81);

        var frame = Decode(raw);

        Assert.Multiple(() =>
        {
            Assert.That(frame.SourceAddress.Address, Is.EqualTo(src.Address));
            Assert.That(frame.DestinationAddress.Address, Is.EqualTo(dst.Address));
        });
    }

    [Test]
    public void Decode_Ctrl1_IsPreserved()
    {
        const byte expectedCtrl1 = 0xAC;
        var raw = BuildRawFrame(CemiLDataFrame.MessageCodeReq, ctrl1: expectedCtrl1, ctrl2: 0xE0,
            sourceAddr: 0x0001, destAddr: 0x0001, apciLowByte: 0x00);
        var frame = Decode(raw);
        Assert.That(frame.Ctrl1, Is.EqualTo(expectedCtrl1));
    }

    [Test]
    public void Decode_AdditionalInfoBytes_AreSkipped()
    {
        // Build a frame that includes 2 additional-info bytes before the rest of cEMI
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)CemiLDataFrame.MessageCodeInd); // message code
        w.Write((byte)2);                             // additional info length = 2
        w.Write((byte)0xAA);                          // additional info byte 1
        w.Write((byte)0xBB);                          // additional info byte 2
        w.Write((byte)0xBC);                          // ctrl1
        w.Write((byte)0xE0);                          // ctrl2
        w.Write((byte)0x00); w.Write((byte)0x01);    // source addr = 0x0001 (big-endian)
        w.Write((byte)0x00); w.Write((byte)0x01);    // dest addr = 0x0001 (big-endian)
        w.Write((byte)1);                             // data length
        w.Write((byte)0x00);                          // TPCI
        w.Write((byte)0x00);                          // APCI low (ValueRead)
        w.Flush();

        var frame = new CemiLDataFrame();
        using var reader = new BinaryReader(ms);
        ms.Position = 0;
        frame.Decode(reader, []);

        Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueRead));
        Assert.That(frame.Ctrl1, Is.EqualTo(0xBC));
    }

    // -------------------------------------------------------------------------
    // Round-trip tests (Encode then Decode)
    // -------------------------------------------------------------------------

    [Test]
    public void RoundTrip_ValueRead_PreservesAllFields()
    {
        var src = new IndividualAddress("1.1.5");
        var dst = new GroupAddress("0/0/1");
        var original = new CemiLDataFrame
        {
            MessageCode = CemiLDataFrame.MessageCodeInd,
            Ctrl1 = 0xBC,
            Ctrl2 = 0xE0,
            SourceAddress = src,
            DestinationAddress = dst,
            EventType = GroupEventType.ValueRead,
            Value = new GroupValue([])
        };

        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.MessageCode, Is.EqualTo(original.MessageCode));
            Assert.That(decoded.Ctrl1, Is.EqualTo(original.Ctrl1));
            Assert.That(decoded.SourceAddress.Address, Is.EqualTo(src.Address));
            Assert.That(decoded.DestinationAddress.Address, Is.EqualTo(dst.Address));
            Assert.That(decoded.EventType, Is.EqualTo(GroupEventType.ValueRead));
            Assert.That(decoded.Value.Value, Is.Empty);
        });
    }

    [Test]
    public void RoundTrip_ValueWrite_SmallData_PreservesAllFields()
    {
        var src = new IndividualAddress("1.1.5");
        var dst = new GroupAddress("5/6/7");
        var original = new CemiLDataFrame
        {
            MessageCode = CemiLDataFrame.MessageCodeReq,
            Ctrl1 = 0xBC,
            Ctrl2 = 0xE0,
            SourceAddress = src,
            DestinationAddress = dst,
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue([0x01])
        };

        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(decoded.Value.Value, Is.EqualTo(original.Value.Value));
            Assert.That(decoded.SourceAddress.Address, Is.EqualTo(src.Address));
            Assert.That(decoded.DestinationAddress.Address, Is.EqualTo(dst.Address));
        });
    }

    [Test]
    public void RoundTrip_ValueResponse_SmallData_PreservesAllFields()
    {
        var original = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueResponse,
            Value = new GroupValue([0x1F])
        };

        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.That(decoded.EventType, Is.EqualTo(GroupEventType.ValueResponse));
        Assert.That(decoded.Value.Value, Is.EqualTo(new byte[] { 0x1F }));
    }

    [Test]
    public void RoundTrip_ValueWrite_LargeData_PreservesAllFields()
    {
        var data = new byte[] { 0x12, 0x34 };
        var original = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue(data)
        };

        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(decoded.Value.Value, Is.EqualTo(data));
        });
    }

    [Test]
    public void RoundTrip_ValueWrite_FourByteData_PreservesValue()
    {
        var data = new byte[] { 0x00, 0x00, 0x01, 0xF4 }; // e.g. DPT-5.001 or float value
        var original = new CemiLDataFrame
        {
            EventType = GroupEventType.ValueWrite,
            Value = new GroupValue(data)
        };

        var encoded = Encode(original);
        var decoded = Decode(encoded);

        Assert.That(decoded.Value.Value, Is.EqualTo(data));
    }

    /// <summary>
    /// Replays a real Wireshark-captured KNX/IP cEMI frame for group address 8/6/1 (DPT 9.001).
    /// cEMI hex: 29 00 9C F0 01 09 46 01 03 00 80 84 12
    /// Verifies the frame is parsed correctly and the raw GroupValue bytes are preserved
    /// exactly as sent on the wire (byte-order agnostic at this layer).
    /// </summary>
    [Test]
    public void Decode_WiresharkCapture_8_6_1_Dpt9()
    {
        // Wireshark: cEMI L_Data.ind, Src=0.1.9, Dst=8/6/1, GroupValueWrite, Data=$8412
        var cemiBytes = new byte[] { 0x29, 0x00, 0x9C, 0xF0, 0x01, 0x09, 0x46, 0x01, 0x03, 0x00, 0x80, 0x84, 0x12 };

        var frame = Decode(cemiBytes);

        Assert.Multiple(() =>
        {
            Assert.That(frame.MessageCode, Is.EqualTo(CemiLDataFrame.MessageCodeInd));
            Assert.That(frame.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(frame.SourceAddress.ToString(), Is.EqualTo("0.1.9"));
            Assert.That(frame.DestinationAddress.Address, Is.EqualTo((ushort)0x4601),
                "Destination must be 8/6/1 = 0x4601");
            Assert.That(frame.Value.Value, Is.EqualTo(new byte[] { 0x84, 0x12 }),
                "GroupValue bytes must be preserved exactly as on the wire");
        });
    }
}
