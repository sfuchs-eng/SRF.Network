using DotMake.CommandLine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Network.WebSocket;

namespace SRF.Network.Cli.Commands;

[CliCommand(Description = "Writes received websocket messages to the console.", Parent = typeof(Root))]
public class ConsoleWriter : HostLauncher<ConsoleWriter.Worker>
{
    [CliOption(Alias = "u", Arity = CliArgumentArity.ExactlyOne, Description = "WebSocket URL to connect to.")]
    public string Url { get; set; } = "ws://localhost:8080";

    public class Worker(
        ConsoleWriter parent,
        IHostApplicationLifetime appLifetime,
        ILogger<JsonWebSocket> wsLogger,
        ILogger<ConsoleWriter.Worker> logger
        ) : Microsoft.Extensions.Hosting.BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            JsonWebSocket? ws = null;
            try
            {
                logger.LogTrace("Creating {wsType} connection to {Url}", nameof(JsonWebSocket), parent.Url);
                ws = new JsonWebSocket(new InsecureWebSocket(), wsLogger);
                ws.WebSocketClosed += (s, e) => logger.LogInformation("WebSocket closed");
                await ws.ConnectAsync(new Uri(parent.Url), stoppingToken);
                logger.LogInformation("Connected to {Url}", parent.Url);

                do
                {
                    var s = await ws.ReceiveStringAsync(stoppingToken);
                    Console.WriteLine(s);
                } while (!stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await (ws?.DisconnectAsync("Client disconnect", stoppingToken) ?? Task.CompletedTask);
                    logger.LogInformation("Disconnected from {Url}", parent.Url);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to disconnect cleanly from {Url}", parent.Url);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to receive JSON messages");
            }

            if ( !stoppingToken.IsCancellationRequested )
                appLifetime.StopApplication();
        }
    }
}
