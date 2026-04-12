using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx;

/// <summary>
/// Represents the context of a KNX message, including the raw value, the time it was received, and the associated event arguments.
/// This class is designed to encapsulate the details of a KNX message for further processing or handling within the application.
/// </summary>
public class KnxMessageContext
{
    public byte[] RawValue => GroupEventArgs?.Value.Value ?? throw new NotImplementedException("IoTGroupEventArgs handling not implemented yet.");
    public DateTimeOffset ReceivedAt { get; }
    public GroupEventArgs? GroupEventArgs { get; set; }
    //public IoTGroupEventArgs? IoTGroupEventArgs { get; set; }

    public KnxMessageContext(GroupEventArgs groupEventArgs, DateTimeOffset receivedAt)
    {
        GroupEventArgs = groupEventArgs ?? throw new ArgumentNullException(nameof(groupEventArgs));
        ReceivedAt = receivedAt;
    }

    /*
        public KnxMessageContext(IoTGroupEventArgs ioTGroupEventArgs, DateTimeOffset? receivedAt = null)
        {
            IoTGroupEventArgs = ioTGroupEventArgs ?? throw new ArgumentNullException(nameof(ioTGroupEventArgs));
            ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow;
            throw new NotImplementedException("IoTGroupEventArgs handling not implemented yet.");
        }
        */
}
