namespace SRF.Network.Knx.Messages;

public enum MessagePriority
{
    /// <summary>
    /// Must not be used for Group Communication
    /// </summary>
    System = 0,

    /// <summary>
    /// Normal priority message
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Alarm priority message
    /// </summary>
    Alarm = 2,

    /// <summary>
    /// Low priority message
    /// </summary>
    Low = 3,
}