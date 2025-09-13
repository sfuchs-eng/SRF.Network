using System;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using WS = System.Net.WebSockets;

namespace SRF.Network.WebSocket;

/// <summary>
/// Skips SSL/TLS certificate validation entirely.
/// </summary>
public class InsecureWebSocket(ILogger logger) : IWebSocketWrapper, IDisposable
{
    private readonly HttpClient httpClient = new(httpClientHandler);

    public Action<HttpRequestHeaders> ConfigureHeaders { get; init; } = (header) => { };

    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    public WS.WebSocket? WebSocketInternal { get; set; }
    public WS.WebSocket WebSocket => WebSocketInternal
        ?? throw new ArgumentNullException(nameof(WebSocketInternal), "No WebSocketInternal object to return. Have you connected?");

    public SemaphoreSlim WebSocketReaderLock { get; } = new(1);

    public SemaphoreSlim WebSocketWriterLock { get; } = new(1);

    public bool IsConnected => (WebSocketInternal?.State ?? WebSocketState.None) == WebSocketState.Open;

    public async Task DisconnectAsync(string reason, CancellationToken cancel)
    {
        var sock = WebSocketInternal;
        WebSocketInternal = null;
        if (sock == null)
            return;
        await sock.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancel);
        sock.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancel)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Add("Connection", "Upgrade");
        req.Headers.Add("Upgrade", "websocket");
        req.Headers.Add("Sec-WebSocket-Version", "13");
        req.Headers.Add("Sec-WebSocket-Key", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
        ConfigureHeaders(req.Headers);
        // req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");

        var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel);

        if (resp.StatusCode != System.Net.HttpStatusCode.SwitchingProtocols)
        {
            throw new WebSocketException($"Got status code {resp.StatusCode} instead of {System.Net.HttpStatusCode.SwitchingProtocols}");
        }

        var stream = await resp.Content.ReadAsStreamAsync(cancel);
        WebSocketInternal = System.Net.WebSockets.WebSocket.CreateFromStream(
            stream,
            isServer: false,
            subProtocol: null,
            keepAliveInterval: KeepAliveInterval
        );
    }

    ~InsecureWebSocket()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
    }

    bool _disposed = false;

    private void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;
        _disposed = true;
        try
        {
            WebSocketInternal?.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "object disposed", CancellationToken.None).Wait();
            httpClient.CancelPendingRequests();
        }
        catch { }
        try
        {
            WebSocketInternal?.Dispose();
        }
        catch { }
        finally
        {
            WebSocketInternal = null;
        }
    }

    private static readonly HttpClientHandler httpClientHandler = new()
    {
        ServerCertificateCustomValidationCallback = (msg, cert, chain, sslPolicyErrors) =>
        {
            return true;
        }
    };
    private readonly ILogger logger = logger;
}
