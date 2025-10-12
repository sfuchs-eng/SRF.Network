using Knx.Falcon;

namespace SRF.Network.Knx;

public class KnxMessageContext
{
    public byte[] RawValue => GroupEventArgs?.Value.Value ?? throw new NotImplementedException("IoTGroupEventArgs handling not implemented yet.");
    public DateTimeOffset ReceivedAt { get; }
    public GroupEventArgs? GroupEventArgs { get; set; }
    public IoTGroupEventArgs? IoTGroupEventArgs { get; set; }

    public KnxMessageContext(GroupEventArgs groupEventArgs, DateTimeOffset? receivedAt = null)
    {
        GroupEventArgs = groupEventArgs ?? throw new ArgumentNullException(nameof(groupEventArgs));
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow;
    }

    public KnxMessageContext(IoTGroupEventArgs ioTGroupEventArgs, DateTimeOffset? receivedAt = null)
    {
        IoTGroupEventArgs = ioTGroupEventArgs ?? throw new ArgumentNullException(nameof(ioTGroupEventArgs));
        ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow;
        throw new NotImplementedException("IoTGroupEventArgs handling not implemented yet.");
    }
}
