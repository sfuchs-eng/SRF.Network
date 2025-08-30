using System;

namespace SRF.Network.WebSocket;

public class JsonReceiverConfig<TJsonObjects> where TJsonObjects : class, new()
{
    public string Url { get; set; } = "ws://localhost:8080";
    public bool Insecure { get; set; } = true;
    public int ReceiveBufferSize { get; set; } = 10 * 1024;

    public int ReconnectDelaySec { get; set; } = 60;
}
