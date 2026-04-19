using SRF.Industrial.Packets;

namespace SRF.Network.Knx.IpRouting;

/// <summary>
/// Wires a <see cref="KnxIpHeader"/> to a <see cref="CemiLDataFrame"/> payload during decode.
/// </summary>
public class KnxIpRoutingPayloadProvider : IPayloadObjectProvider
{
    public bool AssignPayload(IPacket header, bool isResponse = true)
    {
        if (header is KnxIpHeader knxIpHeader && knxIpHeader.ServiceType == KnxIpHeader.RoutingIndicationServiceType)
        {
            knxIpHeader.Payload = new CemiLDataFrame();
            return true;
        }
        return false;
    }
}
