using SRF.Network.Knx.Addressing;

namespace SRF.Network.Knx.Messages;

public class GroupEventArgs : EventArgs
{
    public required GroupAddress DestinationAddress { get; init; }
    public required GroupEventType EventType { get; init; }
    // HopCount
    // IsSecure
    // MessagePriority
    public required IndividualAddress SourceAddress { get; init; }
    public required GroupValue Value { get; init; }
}
