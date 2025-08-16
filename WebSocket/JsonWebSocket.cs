using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WS = System.Net.WebSockets;

namespace SRF.Network.WebSocket;

public class JsonWebSocket : IWebSocketWrapper, IDisposable
{
    private readonly ILogger<JsonWebSocket> logger;

    public System.Net.WebSockets.WebSocket WebSocket => WebSocketWrapper.WebSocket;

    public IWebSocketWrapper WebSocketWrapper { get; init; }

    public event EventHandler<EventArgs>? WebSocketClosed;

    /// <summary>
    /// A <see cref="WS.WebSocket"/> allows only 1 reader and 1 writer at a time.
    /// <see cref="ReceiveAsync{TObject}(CancellationToken)"/> ensures this using this <see cref="SemaphoreSlim"/>.
    /// </summary>
    public SemaphoreSlim WebSocketReaderLock => WebSocketWrapper.WebSocketReaderLock;

    /// <summary>
    /// Ensure Waiting for this <see cref="SemaphoreSlim"/> to guarantee only 1 writer at a time.
    /// </summary>
    public SemaphoreSlim WebSocketWriterLock => WebSocketWrapper.WebSocketWriterLock;

    /// <summary>
    /// Buffer size [bytes] used to receive json object data for deserialization once message is complete.
    /// </summary>
    public int BufferSize { get; set; } = 10 * 1024;

    public JsonSerializerOptions JsonOptionsReceive { get; set; } = new JsonSerializerOptions()
    {
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public JsonSerializerOptions JsonOptionsSend { get; set; } = new JsonSerializerOptions();

    protected virtual async Task<TObject> DeserializeObject<TObject>(byte[] buffer, CancellationToken cancel) where TObject : class, new()
    {
        return await JsonSerializer.DeserializeAsync<TObject>(new MemoryStream(buffer), JsonOptionsReceive, cancel)
            ?? new TObject();
    }

    public async Task<TObject> ReceiveAsync<TObject>(CancellationToken cancel) where TObject : class, new()
    {
        byte[]? buf = null;
        TObject? retObj = null;
        try
        {
            buf = ArrayPool<byte>.Shared.Rent(BufferSize);

            WebSocketReceiveResult result;
            do
            {
                await WebSocketReaderLock.WaitAsync(cancel);
                try
                {
                    result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buf), cancel);
                }
                finally
                {
                    WebSocketReaderLock.Release();
                }

                // handle remote closures
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server side closure", cancel);
                    try
                    {
                        WebSocketClosed?.Invoke(this, new EventArgs());
                        throw new WebSocketException("Closed while receiving.");
                    }
                    catch (Exception exClosedEvent)
                    {
                        logger.LogWarning(exClosedEvent, "WebSocketClosed event failed.");
                    }
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                    logger.LogWarning("Received {wsMsgType}.{wsMsgTypeValue} instead of Text. Proceeding.", nameof(WebSocketMessageType), result.MessageType);
            }
            while (!result.EndOfMessage);

            retObj = await DeserializeObject<TObject>(buf, cancel);
        }
        finally
        {
            if (buf != null)
                ArrayPool<byte>.Shared.Return(buf);
        }

        return retObj ?? throw new JsonException($"Failed to obtain an {typeof(TObject).FullName} object through deserialization.");
    }

    public async Task SendAsync<TObject>(TObject obj, CancellationToken cancel)
    {
        byte[]? buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        await WebSocketWriterLock.WaitAsync(cancel);
        try
        {
            using var bufStream = new MemoryStream(buf);
            await JsonSerializer.SerializeAsync<TObject>(bufStream, obj, JsonOptionsSend, cancel);
            var size = int.CreateChecked(bufStream.Length); // Capacity, _Length_, Position
            await WebSocket.SendAsync(new ArraySegment<byte>(buf, 0, size), WebSocketMessageType.Text, true, cancel);
        }
        finally
        {
            WebSocketWriterLock.Release();
            if (buf != null)
                ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public JsonWebSocket(IWebSocketWrapper webSocketWrapper, ILogger<JsonWebSocket> logger)
    {
        this.logger = logger;
        WebSocketWrapper = webSocketWrapper;
        InitializeJsonOptions();
    }

    protected virtual void InitializeJsonOptions()
    {
        var strEnumConv = new JsonStringEnumConverter();
        JsonOptionsReceive.Converters.Add(strEnumConv);
        JsonOptionsSend.Converters.Add(strEnumConv);
    }

    ~JsonWebSocket()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
    }

    bool _disposed;

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        
    }
}
