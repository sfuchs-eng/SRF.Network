using System;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using WS = System.Net.WebSockets;

namespace SRF.Network.WebSocket;

public class InsecureWebSocket : IWebSocket, IDisposable
{
    private HttpClient? httpClient;

    public Action<HttpRequestHeaders> ConfigureHeaders { get; init; } = (header) => { };

    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    public WS.WebSocket? WebSocket { get; set; }

    public async Task DisconnectAsync(string reason, CancellationToken cancel)
    {
        var sock = WebSocket;
        WebSocket = null;
        if (sock == null)
            return;
        await sock.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancel);
        sock.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancel)
    {
        if (httpClient != null)
        {
            httpClient.CancelPendingRequests();
            httpClient.Dispose();
        }
        httpClient = new HttpClient(httpClientHandler);
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
        WebSocket = System.Net.WebSockets.WebSocket.CreateFromStream(
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
        WebSocket?.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "object disposed", CancellationToken.None).Wait();
        WebSocket?.Dispose();
        httpClient?.CancelPendingRequests();
        httpClient?.Dispose();
    }

    private static readonly HttpClientHandler httpClientHandler = new()
    {
        ServerCertificateCustomValidationCallback = (msg, cert, chain, sslPolicyErrors) =>
        {
            return true;
        }
    };
}
