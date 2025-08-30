using System;

namespace SRF.Network.WebSocket;

public class MessageReceivedEventArgs<TJsonObject>(
    TJsonObject message
) : EventArgs
{
    public TJsonObject Message { get; } = message;
}
