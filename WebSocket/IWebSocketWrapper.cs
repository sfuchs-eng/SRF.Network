using System;
using WS = System.Net.WebSockets;

namespace SRF.Network.WebSocket;

public interface IWebSocketWrapper
{
    /// <summary>
    /// A <see cref="WS.WebSocket"/> allows only 1 reader and 1 writer at a time.
    /// <see cref="ReceiveAsync{TObject}(CancellationToken)"/> ensures this using this <see cref="SemaphoreSlim"/>.
    /// </summary>
    SemaphoreSlim WebSocketReaderLock { get; }
    /// <summary>
    /// Ensure Waiting for this <see cref="SemaphoreSlim"/> to guarantee only 1 writer at a time.
    /// </summary>
    SemaphoreSlim WebSocketWriterLock { get; }

    /// <summary>
    /// The wrapped WebSocket
    /// </summary>
    WS.WebSocket WebSocket { get; }
}
