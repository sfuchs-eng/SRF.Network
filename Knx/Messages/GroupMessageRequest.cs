namespace SRF.Network.Knx.Messages;

/// <summary>
/// Represents a KNX group message to be sent onto the bus.
/// </summary>
public class GroupMessageRequest : IKnxMessage
{
    /// <inheritdoc/>
    public GroupAddress DestinationAddress { get; init; }

    /// <inheritdoc/>
    public GroupValue Value { get; init; }

    /// <inheritdoc/>
    public GroupEventType EventType { get; init; }

    /// <inheritdoc/>
    public MessagePriority Priority { get; init; } = MessagePriority.Low;

    /// <summary>
    /// Creates a <see cref="GroupMessageRequest"/>.
    /// </summary>
    public GroupMessageRequest(GroupAddress destinationAddress, GroupValue value, GroupEventType eventType, MessagePriority priority = MessagePriority.Low)
    {
        DestinationAddress = destinationAddress ?? throw new ArgumentNullException(nameof(destinationAddress));
        Value              = value              ?? throw new ArgumentNullException(nameof(value));
        EventType          = eventType;
        Priority           = priority;
    }

    /// <summary>Creates a GroupValueWrite message.</summary>
    public static GroupMessageRequest Write(GroupAddress destination, GroupValue value, MessagePriority priority = MessagePriority.Low) =>
        new(destination, value, GroupEventType.ValueWrite, priority);

    /// <summary>Creates a GroupValueRead message (no data).</summary>
    public static GroupMessageRequest Read(GroupAddress destination, MessagePriority priority = MessagePriority.Low) =>
        new(destination, new GroupValue([]), GroupEventType.ValueRead, priority);

    /// <summary>Creates a GroupValueResponse message.</summary>
    public static GroupMessageRequest Response(GroupAddress destination, GroupValue value, MessagePriority priority = MessagePriority.Low) =>
        new(destination, value, GroupEventType.ValueResponse, priority);
}
