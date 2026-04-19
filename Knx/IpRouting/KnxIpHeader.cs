using SRF.Industrial.Packets;

namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// KNX/IP 6-byte header (HEADER_10_00_02).
/// Wire format (big-endian):
/// <list type="bullet">
///   <item>Byte 0: Header length = 0x06</item>
///   <item>Byte 1: Protocol version = 0x10</item>
///   <item>Bytes 2-3: Service type identifier (e.g. 0x0530 = Routing Indication)</item>
///   <item>Bytes 4-5: Total packet length including this header</item>
/// </list>
/// </summary>
public class KnxIpHeader : IPacket
{
    public const byte KnxIpHeaderLength = 6;
    public const byte KnxIpProtocolVersion = 0x10;
    public const ushort RoutingIndicationServiceType = 0x0530;

    public ushort ServiceType { get; set; } = RoutingIndicationServiceType;

    /// <summary>Total packet length on the wire (header + payload).</summary>
    public ushort TotalLength => (ushort)(KnxIpHeaderLength + (Payload?.Measure() ?? 0));

    public IPacket? Payload { get; set; }

    public int Measure() => KnxIpHeaderLength + (Payload?.Measure() ?? 0);

    public void Encode(BinaryWriter writer)
    {
        // Use a swapping writer scoped to this header's bytes for big-endian output.
        var sw = new SwappingBinaryWriter(writer.BaseStream, swap: BitConverter.IsLittleEndian);
        writer.Write(KnxIpHeaderLength);         // 1 byte — no swap needed
        writer.Write(KnxIpProtocolVersion);      // 1 byte — no swap needed
        sw.Write(ServiceType);                   // 2 bytes big-endian
        sw.Write(TotalLength);                   // 2 bytes big-endian
        Payload?.Encode(writer);
    }

    public void Decode(BinaryReader reader, IEnumerable<IPayloadObjectProvider> payloadProviders)
    {
        var sr = new SwappingBinaryReader(reader.BaseStream, swap: BitConverter.IsLittleEndian);
        byte headerLen = reader.ReadByte();
        byte version   = reader.ReadByte();

        if (headerLen != KnxIpHeaderLength)
            throw new InvalidDataException($"Unexpected KNX/IP header length {headerLen}, expected {KnxIpHeaderLength}.");
        if (version != KnxIpProtocolVersion)
            throw new InvalidDataException($"Unsupported KNX/IP protocol version 0x{version:X2}, expected 0x{KnxIpProtocolVersion:X2}.");

        ServiceType = sr.ReadUInt16();
        ushort totalLength = sr.ReadUInt16();
        _ = totalLength; // used externally to frame the read; payload decode is driven by the provider

        if (payloadProviders.Any(pp => pp.AssignPayload(this)))
            Payload?.Decode(reader, payloadProviders);
    }
}
