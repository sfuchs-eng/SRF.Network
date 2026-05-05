using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.OpenHab.Client
{
    internal class PingPongWatchDog
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="T:SRF.Network.OpenHab.Client.PingPongWatchDog"/> class.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="pingSecs">Ping the server every x seconds</param>
        /// <param name="timeoutSecs">If there's no Pong y secs after the Ping, call the timeoutHandler and return from <see cref="Run(CancellationToken)"/></param>
        /// <param name="timeoutHandler">Method processing the occurred watchdog timeout</param>
        public PingPongWatchDog(EventBusClient client, Action<PingPongWatchDog> timeoutHandler, ILogger logger, int pingSecs = 5, int timeoutSecs = 3)
        {
            Client = client;
            TimeoutHandler = timeoutHandler;
            Logger = logger;
            PingSecs = pingSecs;
            PongTimeout = new System.Timers.Timer()
            {
                Interval = timeoutSecs * 1000,    // time out connecting if a ping is not answered by a pong within ... ms
                AutoReset = false,
                Enabled = false
            };
            PongTimeout.Elapsed += PongTimeout_Elapsed;
        }

        public readonly EventBusClient Client;
        readonly ILogger Logger;
        readonly Action<PingPongWatchDog> TimeoutHandler;
        readonly int PingSecs;

        readonly System.Timers.Timer PongTimeout; // Watchdog barks if timer elapses.

        CancellationTokenSource? InternalTokenSource { get; set; }
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Terminates in the following cases:
        /// a) Watchdog timeout occurred: <see cref="TimedOut"/> is set to <code>true</code>
        /// b) WebSocket reaches states Closed or Aborted. <see cref="TimedOut"/> is <code>false</code> if there was not ping-pong timeout
        /// c) <see cref="Stop"/> is called. <see cref="TimedOut"/> is <code>false</code> if there was not ping-pong timeout
        /// </summary>
        public async Task Run(CancellationToken token)
        {
            Logger.LogTrace("Starting WatchDog...");
            InternalTokenSource?.Cancel(); // ensure nothing remains hanging in case Run is called multiple times. --> may result in a watchdog timeout
            PongTimeout.Stop();
            InternalTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            TimedOut = false;
            await Task.WhenAll(new Task[] {
                PingTransmitter(InternalTokenSource.Token)
            });
            Stop();
            PongTimeout.Stop();
            if ( TimedOut )
            {
                TimeoutHandler?.Invoke(this);
            }
            InternalTokenSource.Cancel();
            InternalTokenSource.Dispose();
            InternalTokenSource = null;
            Logger.LogTrace("Watchdog terminated.");
        }

        public void Stop()
        {
            InternalTokenSource?.Cancel();
        }

        void PongTimeout_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            TimedOut = true;
            InternalTokenSource?.Cancel();
        }

        private void PongEventHandler(object sender, EventReceivedEventArgs args)
        {
            if (sender != this || args.Received.Type != EventType.WebSocketEvent || !((args.Received as WebSocketEvent)?.IsPong ?? false))
                return;
            // it's a PONG -- stop timer before it elapses and the watchdog signals failure.
            PongTimeout.Stop();
        }

        private async Task PingTransmitter(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (Client.WSClient?.State == WebSocketState.Open)
                {
                    var ping = Client.EventFactory.CreatePing();
                    await Client.SendAsync(ping, token);
                    if (!PongTimeout.Enabled)
                        PongTimeout.Start();
                }
                if ( Client.WSClient == null || Client.WSClient?.State == WebSocketState.Closed || Client.WSClient?.State == WebSocketState.Aborted)
                    break;
                await Task.Delay(5000, token);
            }
        }
    }
}
