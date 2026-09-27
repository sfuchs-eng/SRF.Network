using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SRF.Network.OpenHab
{
    /// <summary>
    /// Wraps an <see cref="IEventBusClient"/> in a <see cref="IHostedService"/>
    /// that automatically reconnects in case the client disconnects unexpectedly.
    /// Ensure <see cref="IEventBusClient"/> and <see cref="IEventFactory"/> are both singleton services.
    /// </summary>
    public sealed class OpenHabConnector : IHostedService
    {
        public OpenHabConnector(IEventBusClient client, ILogger<OpenHabConnector> logger)
        {
            Client = client;
            Logger = logger;
        }

        private IEventBusClient Client { get; set; }
        private ILogger<OpenHabConnector> Logger { get; set; }
        private CancellationTokenSource? StopConnector { get; set; }
        private Task? RunnerTask { get; set; }

        /// <summary>
        /// Wait ms before trying to connect again.
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsRunning)
                throw new Client.ConnectionException("Double start attempt.");
            Logger.LogTrace("Starting OpenHAB connector background loop (ReconnectInterval={ReconnectIntervalMs} ms).", ReconnectInterval);
            StopConnector = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            RunnerTask = Run(StopConnector.Token);
            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogTrace("Stopping OpenHAB connector background loop.");
            StopConnector?.Cancel();
            try
            {
                await (RunnerTask ?? Task.CompletedTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Logger.LogDebug("OpenHabConnector shutdown was canceled by host timeout.");
            }
            catch (OperationCanceledException)
            {
                Logger.LogTrace("OpenHabConnector terminated by cancellation.");
            }
            RunnerTask = null;
            Logger.LogTrace("OpenHAB connector background loop stopped.");
        }

        public bool IsRunning {
            get {
                var completedStates = new TaskStatus[] { TaskStatus.Canceled, TaskStatus.Faulted, TaskStatus.RanToCompletion };
                return RunnerTask != null && !completedStates.Any(s => s == RunnerTask.Status);
            }
        }

        private async Task Run(CancellationToken cancel)
        {
            while ( !cancel.IsCancellationRequested )
            {
                try
                {
                    Logger.LogTrace("Attempting OpenHAB websocket connect.");
                    await Client.ConnectAsync(cancel);
                    Logger.LogTrace("OpenHAB websocket session ended. Reconnect loop continues unless stopping.");
                }
                catch ( OperationCanceledException )
                {
                    Logger.LogTrace("Terminated.");
                    return;
                }
                catch ( Exception ex )
                {
                    Logger.LogWarning(ex, "OpenHAB connection failed. Reconnecting in {reconnectWaitInterval} s.", ReconnectInterval/1000.0);
                }

                try
                {
                    await Task.Delay(ReconnectInterval, cancel);
                }
                catch ( OperationCanceledException )
                {
                    Logger.LogTrace("OpenHabConnector reconnect wait canceled.");
                    return;
                }
            }
        }
    }
}
