using System;

namespace SRF.Network.Knx.Messages;

/// <summary>
/// KNX Group event types supported by the library,
/// used in <see cref="GroupEventArgs"/> to indicate the type of group event (e.g., read, write, response).
/// </summary>
public enum GroupEventType
{
    ValueRead = 0,
    ValueWrite = 1,
    ValueResponse = 2,
}
