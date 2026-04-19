namespace SRF.Network.Knx.Messages;

/// <summary>
/// KNX group event properties for e.g.
/// GroupValueWrite, GroupValueRead, GroupValueResponse events.
/// </summary>
public class GroupEventArgs : EventArgs
{
    public required GroupAddress DestinationAddress { get; init; }
    public required GroupEventType EventType { get; init; }
    // HopCount
    // IsSecure
    public MessagePriority? Priority { get; init; } = MessagePriority.Low;
    public required IndividualAddress SourceAddress { get; init; }
    public required GroupValue Value { get; init; }
}
