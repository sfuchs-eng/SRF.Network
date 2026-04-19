using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx;

/// <summary>
/// Represents a KNX message to be sent on the bus.
/// </summary>
public interface IKnxMessage
{
    /// <summary>The destination group address.</summary>
    GroupAddress DestinationAddress { get; }

    /// <summary>The raw KNX payload value.</summary>
    GroupValue Value { get; }

    /// <summary>The type of group event (Read, Write, or Response).</summary>
    GroupEventType EventType { get; }

    /// <summary>The message priority. Defaults to <see cref="MessagePriority.Low"/>.</summary>
    MessagePriority Priority { get; }
}