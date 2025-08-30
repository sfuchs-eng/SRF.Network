using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SRF.Network.WebSocket;

public class JsonReceiver<TJsonObjects> : BackgroundService, IDisposable where TJsonObjects : class, new()
{
    public JsonReceiver(
        IOptions<JsonReceiverConfig<TJsonObjects>> options,
        ILogger<InsecureWebSocket> insecureWsLogger,
        ILogger<JsonWebSocket> wsLogger,
        ILogger<JsonReceiver<TJsonObjects>> logger
    ) : base()
    {
        this.logger = logger;
        this.config = options?.Value ?? new JsonReceiverConfig<TJsonObjects>();
        if (!this.config.Insecure)
        {
            throw new NotImplementedException("Secure WebSocket connections with certificate validation are not implemented yet. Insecure = true and wss:// url results in an encrypted connection without certificate validation.");
        }

        jsonWebSocket = new JsonWebSocket(new InsecureWebSocket(insecureWsLogger)
        {
            ConfigureHeaders = (headers) =>
            {
                headers.Add("User-Agent", "SRF.Network.JsonReceiver");
            }
        }, wsLogger)
        {
            BufferSize = config.ReceiveBufferSize
        };
    }

    protected JsonWebSocket jsonWebSocket;

    protected readonly JsonReceiverConfig<TJsonObjects> config;

    private readonly ILogger<JsonReceiver<TJsonObjects>> logger;

    public event EventHandler<MessageReceivedEventArgs<TJsonObjects>>? MessageReceived;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Connect if not connected
            if (!jsonWebSocket.WebSocket.State.HasFlag(System.Net.WebSockets.WebSocketState.Open))
            {
                try
                {
                    logger.LogTrace("Connecting to {Url}, current state {State}", config.Url, jsonWebSocket.WebSocket.State);
                    var uri = new Uri(config.Url);
                    await jsonWebSocket.ConnectAsync(uri, stoppingToken);
                    logger.LogInformation("Connected to {Uri}", uri);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to connect to {Url}", config.Url);
                    await Task.Delay(TimeSpan.FromSeconds(config.ReconnectDelaySec), stoppingToken);
                    continue;
                }
            }

            try
            {
                var obj = await jsonWebSocket.ReceiveAsync<TJsonObjects>(stoppingToken);
                MessageReceived?.GetInvocationList().AsParallel().ForAll(d => {
                    try
                    {
                        d.DynamicInvoke(this, new MessageReceivedEventArgs<TJsonObjects>(obj));
                    }
                    catch (Exception exInv)
                    {
                        logger.LogError(exInv, "Failed to invoke MessageReceived handler");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Ignore, we're stopping
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to receive JSON object");
                try
                {
                    await jsonWebSocket.DisconnectAsync("Error receiving JSON object", stoppingToken);
                }
                catch (Exception dex)
                {
                    logger.LogError(dex, "Failed to disconnect after receive error");
                }
                await Task.Delay(TimeSpan.FromSeconds(config.ReconnectDelaySec), stoppingToken);
            }
        }
        logger.LogTrace("Terminating service.");
    }

    ~JsonReceiver()
    {
        Dispose(false);
    }

    public override void Dispose()
    {
        base.Dispose();
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool _disposed = false;

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            try
            {
                jsonWebSocket?.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
