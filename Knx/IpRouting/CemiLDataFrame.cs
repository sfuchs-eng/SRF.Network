using SRF.Industrial.Packets;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// cEMI L_DATA frame as used in KNX/IP Routing Indication (service type 0x0530).
/// <para>Wire layout:</para>
/// <list type="bullet">
///   <item>Byte 0: Message code (0x29 = L_DATA.ind / 0x11 = L_DATA.req)</item>
///   <item>Byte 1: Additional info length (0x00)</item>
///   <item>Byte 2: Ctrl1</item>
///   <item>Byte 3: Ctrl2 (bit 7 = 1: destination is group address; bits 6–4: hop count)</item>
///   <item>Bytes 4–5: Source individual address (big-endian)</item>
///   <item>Bytes 6–7: Destination address (big-endian)</item>
///   <item>Byte 8: Data length = APDU byte count – 1</item>
///   <item>Byte 9: TPCI / APCI high byte</item>
///   <item>Byte 10: APCI low byte (also carries small data ≤ 6 bits in low 6 bits)</item>
///   <item>Bytes 11+: Remaining data bytes for payloads &gt; 6 bits</item>
/// </list>
/// </summary>
public class CemiLDataFrame : IPacket
{
    /// <summary>Message code for L_DATA.ind (received from bus).</summary>
    public const byte MessageCodeInd = 0x29;
    /// <summary>Message code for L_DATA.req (to send on bus).</summary>
    public const byte MessageCodeReq = 0x11;

    // APCI service codes (upper 10 bits of the 2-byte TPCI/APCI field, masked with 0x03C0)
    private const ushort ApciGroupValueRead     = 0x0000;
    private const ushort ApciGroupValueResponse = 0x0040;
    private const ushort ApciGroupValueWrite    = 0x0080;
    private const ushort ApciMask               = 0x03C0;

    private const byte DefaultCtrl1     = 0xBC; // standard frame, no repeat, normal priority, no ACK
    private const byte DefaultCtrl2Daf  = 0xE0; // dest=group (bit 7), hop count 6 (bits 6-4)
    private const int  FixedHeaderBytes = 10;   // everything up to and including the APCI low byte

    public byte MessageCode { get; set; } = MessageCodeReq;
    public byte Ctrl1 { get; set; } = DefaultCtrl1;
    public byte Ctrl2 { get; set; } = DefaultCtrl2Daf;
    public IndividualAddress SourceAddress { get; set; } = new IndividualAddress("0.0.1");
    public GroupAddress DestinationAddress { get; set; } = new GroupAddress();
    public GroupEventType EventType { get; set; } = GroupEventType.ValueWrite;
    public GroupValue Value { get; set; } = new GroupValue([]);

    // No nested payload — cEMI is the innermost layer.
    public IPacket? Payload => null;

    public int Measure()
    {
        int dataLen = Value.Value.Length;
        // 0-byte payloads (GroupValueRead) still occupy the APCI low byte (data length field = 1)
        // APDU = TPCI(1) + APCI_high(0, merged into TPCI byte) + APCI_low(1) + extra data
        // DataLength field = number of APDU bytes after TPCI = 1 + extraDataBytes
        int extraDataBytes = dataLen > 0 ? dataLen : 0;
        return FixedHeaderBytes + extraDataBytes;
    }

    public void Encode(BinaryWriter writer)
    {
        var sw = new SwappingBinaryWriter(writer.BaseStream, swap: BitConverter.IsLittleEndian);

        bool smallData = Value.Value.Length == 0 || (EventType != GroupEventType.ValueRead && Value.Value.Length == 1 && (Value.Value[0] & 0xC0) == 0);
        byte[] extraBytes = smallData ? [] : Value.Value;

        // DataLength = number of APDU bytes after the TPCI byte
        // APDU structure: [TPCI+APCI_high (1 byte)] [APCI_low+data (1 byte)] [extra data...]
        byte dataLength = (byte)(1 + extraBytes.Length);

        ushort apci = EventType switch
        {
            GroupEventType.ValueRead     => ApciGroupValueRead,
            GroupEventType.ValueResponse => ApciGroupValueResponse,
            GroupEventType.ValueWrite    => ApciGroupValueWrite,
            _ => throw new InvalidOperationException($"Unsupported GroupEventType: {EventType}")
        };

        byte apciLow = (byte)(apci & 0x3F);
        if (smallData && EventType != GroupEventType.ValueRead && Value.Value.Length == 1)
            apciLow |= (byte)(Value.Value[0] & 0x3F);

        writer.Write(MessageCode);
        writer.Write((byte)0x00);     // additional info length
        writer.Write(Ctrl1);
        writer.Write(Ctrl2);
        sw.Write(SourceAddress.Address);
        sw.Write(DestinationAddress.Address);
        writer.Write(dataLength);
        writer.Write((byte)0x00);     // TPCI (data group, no sequence number)
        writer.Write(apciLow);        // APCI low byte (includes small data)

        foreach (byte b in extraBytes)
            writer.Write(b);
    }

    public void Decode(BinaryReader reader, IEnumerable<IPayloadObjectProvider> payloadProviders)
    {
        var sr = new SwappingBinaryReader(reader.BaseStream, swap: BitConverter.IsLittleEndian);

        MessageCode = reader.ReadByte();
        byte additionalInfoLen = reader.ReadByte();
        if (additionalInfoLen > 0)
            reader.ReadBytes(additionalInfoLen); // skip

        Ctrl1 = reader.ReadByte();
        Ctrl2 = reader.ReadByte();

        SourceAddress      = new IndividualAddress(sr.ReadUInt16());
        DestinationAddress = new GroupAddress(sr.ReadUInt16());

        byte dataLength = reader.ReadByte();        // APDU length - 1
        byte tpci      = reader.ReadByte();         // TPCI byte (ignored, always 0x00 for data group)
        byte apciLow   = reader.ReadByte();

        // Reconstruct APCI service code from TPCI byte upper bits + apciLow upper 2 bits
        ushort apciService = (ushort)(((tpci & 0x03) << 6) | (apciLow & 0xC0));
        apciService &= ApciMask; // mask to service bits only (use 0x03C0 to handle both nibbles)

        EventType = (apciService & 0x00C0) switch
        {
            ApciGroupValueRead     => GroupEventType.ValueRead,
            ApciGroupValueResponse => GroupEventType.ValueResponse,
            ApciGroupValueWrite    => GroupEventType.ValueWrite,
            _ => GroupEventType.ValueWrite  // fallback
        };

        // dataLength field counts bytes after TPCI: 1 means only APCI low byte, no extra data.
        int extraBytes = dataLength - 1;

        if (EventType == GroupEventType.ValueRead || extraBytes == 0)
        {
            // Small data (≤ 6 bits) is packed in the low 6 bits of the APCI low byte.
            // GroupValueRead has no data — return empty.
            byte smallVal = (byte)(apciLow & 0x3F);
            Value = EventType == GroupEventType.ValueRead
                ? new GroupValue([])
                : new GroupValue([smallVal]);
        }
        else
        {
            byte[] data = reader.ReadBytes(extraBytes);
            Value = new GroupValue(data);
        }
    }
}
