using System;

namespace SRF.Network.Knx.Messages;

public enum GroupEventType
{
    ValueRead = 0,
    ValueWrite = 1,
    ValueResponse = 2,
}
