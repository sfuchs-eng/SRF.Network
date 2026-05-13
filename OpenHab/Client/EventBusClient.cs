using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SRF.Network.OpenHab.EventBus;
using System.Text.Json;
using System.IO;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.OpenHab.Client;

/// <summary>
/// A WebSocket based OpenHAB client to connect to an OpenHAB instance's event bus.
/// </summary>
public class EventBusClient : IEventBusClient
{
    private readonly TimeProvider _timeProvider;

    protected EventBusClientOptions Options { get; set; }
    protected ILogger Logger { get; set; }
    public IEventFactory EventFactory { get; set; }
    public ClientWebSocket? WSClient { get; protected set; }
    private PingPongWatchDog WatchDog { get; set; }

    public bool IsConnected { get => WSClient?.State == WebSocketState.Open; }

    protected ArraySegment<byte> Buffer { get; set; }
    //protected JsonDocumentOptions ReceivingJsonOptions { get; set; }

    protected int TransmitPacketCounter { get; set; } = 0;

    public string[] AppliedSourceFilters { get; protected set; } = [];
    public EventType[] AppliedTypeFilters { get; protected set; } = [];

    private ManualResetEventSlim WebSocketReady { get; } = new ManualResetEventSlim(false);
    private ManualResetEventSlim WebSocketReconnectRequired { get; set; } = new ManualResetEventSlim(false);

    public EventBusClient(IOptions<EventBusClientOptions> options, IEventFactory eventFactory, ILogger<EventBusClient> logger, TimeProvider? timeProvider = null)
    {
        logger.LogDebug("Initializing...");
        Options = options.Value;
        Logger = logger;
        EventFactory = eventFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        WatchDog = new PingPongWatchDog(this, WatchDogTimeoutHandler, logger);
        EventReceived += EventBusClient_EventReceived;

        Buffer = WebSocket.CreateClientBuffer(Options.ClientBufferSize, Options.ClientBufferSize);
    }

    private void WatchDogTimeoutHandler(PingPongWatchDog obj)
    {
        Logger.LogWarning("OpenHAB event bus websocket connection timed out. No pong response from server. Trying to close with 5s timeout.");
        Task.Run(async () => await CloseAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token));
    }

    protected Uri RequestURI { get => new Uri(Options.WebSocket + "?accessToken=" + Options.AccessToken); }

    private List<Task> ProcessingTasks { get; set; } = new List<Task>();
    protected CancellationTokenSource StopProcessingTokenSource { get; private set; } = new CancellationTokenSource();

    /// <summary>
    /// <see langword="true"/> if receiving/transmitting tasks are likely active.
    /// </summary>
    public bool IsActive { get; private set; } = false;

    /// <summary>
    /// Connects to the OpenHAB websocket and completes only after closure / failure / cancellation.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enable)
        {
            Logger.LogWarning("OpenHAB Event Bus Client is disabled by configuration - not connecting. Queues will accumulate infinitely.");
            while (!cancellationToken.IsCancellationRequested)
                await Task.Delay(1000 * 60, cancellationToken);
            // must only return once connection gets closed --> Task canceled.
            return;
        }

        /*
        var requireState = new WebSocketState[] { WebSocketState.Aborted, WebSocketState.Closed, WebSocketState.None };
        if (!requireState.Any(rs => rs == WSClient.State))
        {
            var needStatesString = string.Join(", ", requireState.Select(rs => rs.ToString()));
            Logger.LogError("Refused to connect. Need any of {reqStates} instead of {currentState}.", needStatesString, WSClient.State);
            throw new ConnectionException($"Cannot connect if in state {WSClient.State}; need any of {needStatesString}");
        }*/
        StopProcessingTokenSource?.Cancel();
        IsActive = true;
        StopProcessingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken[] { cancellationToken });

        if ( ReceivingQueue == null || ReceivingQueue.IsAddingCompleted )
            ReceivingQueue = new BlockingCollection<EventReceivedEventArgs>();
        if ( SendingQueue == null || SendingQueue.IsAddingCompleted )
            SendingQueue = new BlockingCollection<IEvent>();

        await CreateAndConnectWebSocket(cancellationToken);
        
        // Signal that the WebSocket is ready for receiving/transmitting
        WebSocketReady.Set();

        // receive, transmit, ... do things until closure
        KeyValuePair<string, Task>[] ptsk = Array.Empty<KeyValuePair<string, Task>>();
        try
        {
            ptsk = new KeyValuePair<string, Task>[]
            {
                new KeyValuePair<string, Task>(nameof(WebSocketRecreator), WebSocketRecreator(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(ReceivingNotifierAsync), ReceivingNotifierAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(ReceivingLoopAsync), ReceivingLoopAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(TransmittingLoopAsync), TransmittingLoopAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(WatchDog.Run), WatchDog.Run(StopProcessingTokenSource.Token))
            };
            ProcessingTasks.AddRange(ptsk.Select(p => p.Value));
            await Task.WhenAll(ptsk.Select(p => p.Value));
            IsActive = false;
        }
        catch ( OperationCanceledException )
        {
        }
        catch ( Exception e )
        {
            Logger.LogError("OpenHAB websocket communication aborted with exception {exType}: {exMsg}\n{StackTrace}\n", e.GetType().Name, e.Message, e.StackTrace);
            Logger.LogDebug("Tasks stati: {statiList}", string.Join(", ", ptsk.Select(t => $"{t.Key}:{t.Value.Status.ToString()}")));
            StopProcessingTokenSource.Cancel();
            IsActive = false;
            throw new ConnectionException("OpenHAB websocket comm aborted with exception (see inner)", e);
        }
    }

    private bool IsWebSocketConnectedByState { get => WSClient?.State == WebSocketState.Open || WSClient?.State == WebSocketState.Connecting; }

    private async Task CreateAndConnectWebSocket(CancellationToken cancellationToken)
    {
        try
        {
            WSClient = new ClientWebSocket();
            await WSClient.ConnectAsync(RequestURI, cancellationToken);
        }
        catch (OperationCanceledException oce)
        {
            IsActive = false;
            throw new ConnectionException("Connecting cancelled.", oce);
        }
        catch (Exception ex)
        {
            Logger.LogTrace(ex, "Failed to connect to '{RequestURI}'", RequestURI);
            Logger.LogError(ex, "Failed to connect to OpenHAB server at {ServerURI} with access token.", Options.WebSocket);
            IsActive = false;
            throw new ConnectionException("Connecting failed.", ex);
        }

        if (!IsWebSocketConnectedByState)
        {
            IsActive = false;
            throw new ConnectionException($"Failed to connect to OpenHAB at '{Options.WebSocket}' with access token.");
        }

        // connection config
        if (Options.FilterSource)
        {
            // don't want to hear myself...
            _ = SetSourceFilterAsync(new string[] { Options.SourceEntity });
        }
    }

    private void TriggerWebSocketRecreationAndReconnection()
    {
        WebSocketReady.Reset();
        WebSocketReconnectRequired.Set();
    }

    private async Task WebSocketRecreator(CancellationToken cancellationToken)
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            WebSocketReconnectRequired.Wait(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                break;

            // try closure
            try
            {
                await (WSClient?.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Reinstanciation and reconnect requested by client.", cancellationToken)
                    ?? Task.CompletedTask);
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "WebSocket closure failed. Creating new one.");
            }

            // recreate, reconnect
            try
            {
                Logger.LogTrace("Reconnecting WebSocket...");
                await CreateAndConnectWebSocket(cancellationToken);
                if ( !IsWebSocketConnectedByState )
                {
                    Logger.LogWarning("Newly created and connected WebSocket State = {webSocketState} indicated reconnect failure. Waiting and retrying...",
                        WSClient?.State);
                    await Task.Delay(Options.ReconnectWaitTime, cancellationToken);
                    continue;
                }
                Logger.LogTrace("Reconnect successful.");
                WebSocketReconnectRequired.Reset();
                WebSocketReady.Set();
            }
            catch ( Exception e2)
            {
                Logger.LogError(e2, "Failed to recreate WebSocket or reconnect. Waiting and retrying...");
                await Task.Delay(Options.ReconnectWaitTime);
                continue;
            }
        }
    }

    /// <summary>
    /// Occurs when event received.
    /// </summary>
    public event EventHandler<EventReceivedEventArgs> EventReceived;

    /// <summary>
    /// Fires receiving events for events in the ReceivingQueue.
    /// </summary>
    protected async Task ReceivingNotifierAsync(CancellationToken cancellation)
    {
        await Task.Run(() =>
        {
            Logger.LogTrace("Starting event notifier loop...");
            try
            {
                EventReceivedEventArgs? cur = null;
                while (!ReceivingQueue.IsCompleted && !cancellation.IsCancellationRequested)
                {
                    try
                    {

                        cur = ReceivingQueue.Take(cancellation);
                        if (cur != null && !cancellation.IsCancellationRequested)
                        {
                            Logger.LogDebug("OpenHAB event received: {IEvent}", cur.Received.ToString());
                            EventReceived?.Invoke(this, cur);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning("Processing of received IEvent {eventText} failed with exception {exceptionType}: {exceptionMessage}", cur?.ToString(), e.GetType().Name, e.Message);
                    }
                }
            }
            catch ( Exception ex )
            {
                Logger.LogDebug(ex, "{funcName} died.", nameof(ReceivingNotifierAsync));
                throw new ConnectionException("Receiving failed.", ex);
            }
        }, cancellation);
    }

    /// <summary>
    /// Keeps receiving until aborted
    /// </summary>
    protected async Task ReceivingLoopAsync(CancellationToken cancellation)
    {
        await Task.Run(async () =>
        {
            Logger.LogTrace("Starting receiving loop...");
            while ( !ReceivingQueue.IsCompleted && !cancellation.IsCancellationRequested )
            {
                while ( !WebSocketReady.IsSet )
                    await Task.Delay(100);

                if (WSClient?.State != WebSocketState.Open)
                {
                    Logger.LogWarning("Websocket not in state Open. Requesting reconnection.");
                    TriggerWebSocketRecreationAndReconnection();
                    continue;
                }

                try
                {
                    var evt = await ReceiveAsync(cancellation);
                    if (ReceivingQueue.IsCompleted || cancellation.IsCancellationRequested)
                        break;
                    if (evt == null)
                        continue;
                    if (evt.Type == EventType.WebSocketEvent && evt is WebSocketEvent wse && wse.IsResponseFailed)
                        Logger.LogError("Failure response: {evt}", wse.ToString());
                    ReceivingQueue.Add(new EventReceivedEventArgs(evt, _timeProvider.GetUtcNow()));
                }
                catch ( InvalidOperationException )
                {
                    // JsonSerializer throws this in case of cancellation.
                    if (cancellation.IsCancellationRequested)
                        break;
                }
                catch ( OperationCanceledException )
                {
                    break;
                }
                catch ( Exception e )
                {
                    Logger.LogDebug(e, "OpenHAB event reception failed");
                }
            }
            Logger.LogTrace("Receiving loop exited.");
        },
        cancellation);
    }

    protected BlockingCollection<IEvent> SendingQueue { get; set; } = new BlockingCollection<IEvent>();
    protected BlockingCollection<EventReceivedEventArgs> ReceivingQueue { get; set; } = new BlockingCollection<EventReceivedEventArgs>();

    public void EnqueueTransmit(IEvent sendEvent)
    {
        SendingQueue.Add(sendEvent);
    }

    /// <summary>
    /// Transmits whatever comes into the <see cref="SendingQueue"/> via <see cref="EnqueueTransmit(IEvent)"/>.
    /// </summary>
    private async Task TransmittingLoopAsync(CancellationToken token)
    {
        await Task.Run(async () =>
        {
            Logger.LogTrace("Waiting for connection to become ready...");
            // there's a glitch if sending too early... status alone doesn't seem sufficient.
            await Task.Delay(1000);

            Logger.LogTrace("Starting transmitting loop...");
            while ( !SendingQueue.IsCompleted && !token.IsCancellationRequested )
            {
                // check & wait until websocket ready
                while (WSClient?.State != WebSocketState.Open && !token.IsCancellationRequested)
                {
                    while (!WebSocketReady.IsSet && !token.IsCancellationRequested)
                    {
                        // assume websocket is being connected, wait and try again.
                        await Task.Delay(1000);
                    }
                }
                if (token.IsCancellationRequested)
                    return;

                // fetch item from sending queue and transmit.
                var txEvent = SendingQueue.Take(token);
                try
                {
                    await this.SendAsync(txEvent, token);
                }
                catch (WebSocketException we)
                {
                    // close websocket and connect with new one.
                    Logger.LogWarning(we, "Sending IEvent {eventText} failed. Triggering reconnecting...", txEvent?.ToString());
                    TriggerWebSocketRecreationAndReconnection();
                }
                catch ( Exception e )
                {
                    Logger.LogWarning(e, "Sending IEvent {eventText} failed. Triggering reconnecting...", txEvent?.ToString());
                    TriggerWebSocketRecreationAndReconnection();
                }
            }
            Logger.LogTrace("Transmitting loop exited.");
        }, token);
    }

    protected async Task<IEvent> ReceiveAsync(CancellationToken cancellationToken)
    {
        if ( WSClient?.State != WebSocketState.Open )
        {
            throw new ConnectionException($"{nameof(ReceiveAsync)} requires the websocket to be in status Open instead of {WSClient?.State}");
        }
        // receiving: https://stackoverflow.com/questions/44738862/how-to-decode-websocket-connect-as-a-json-stream

        var cts = CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken[] { cancellationToken });

        var message = new MemoryStream(2048);

        WebSocketReceiveResult? res = null;
        do
        {
            res = await WSClient.ReceiveAsync(Buffer, cts.Token);
            if (res.Count > 0)
                await message.WriteAsync(Buffer.Array ?? throw new ConnectionException("Failed to get buffer array for receiving."), Buffer.Offset, res.Count, cts.Token);
        } while (!(res?.EndOfMessage ?? true || cancellationToken.IsCancellationRequested ));

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException();

        return EventFactory.Create(message);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Logger.LogTrace("Closing connection...");
        StopProcessingTokenSource.Cancel();
        await Task.WhenAll(ProcessingTasks);
        ProcessingTasks = new List<Task>(10);
        await (WSClient?.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Regular closure", cancellationToken)
            ?? Task.CompletedTask);
        Logger.LogTrace("Connection closed.");
    }

    public async Task SendAsync(IEvent sendEvent, CancellationToken cancellationToken)
    {
        var mcts = CancellationTokenSource.CreateLinkedTokenSource(StopProcessingTokenSource.Token, cancellationToken);
        sendEvent.ID = (++TransmitPacketCounter).ToString();
        sendEvent.Source = Options.SourceEntity;
        var serEvt = EventFactory.Serialize(sendEvent);
        Logger.LogDebug("Transmitting {evtClass} event: {jsonEvent}", sendEvent.GetType().Name,
            System.Text.Encoding.UTF8.GetString(serEvt.ToArray()));
        await (WSClient?.SendAsync(
            serEvt,
            WebSocketMessageType.Text,
            true, mcts.Token
            ) ?? throw new ConnectionException($"No websocket object for sending {nameof(IEvent)}."));
    }

    /// <summary>
    /// Sends the <paramref name="packet"/> UTF8 encoded through the websocket.
    /// </summary>
    public async Task SendAsync(string packet, CancellationToken cancel)
    {
        var mcts = CancellationTokenSource.CreateLinkedTokenSource(StopProcessingTokenSource.Token, cancel);
        await (WSClient?.SendAsync(
            new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(packet)),
            WebSocketMessageType.Text,
            true, mcts.Token
        ) ?? throw new ConnectionException($"No websocket object for sending packet '{packet}'"));
    }

    void EventBusClient_EventReceived(object? sender, EventReceivedEventArgs e)
    {
        switch ( e.Received.Type )
        {
            case EventType.WebSocketEvent:
                if (e.Received is WebSocketEvent evt)
                {
                    if (evt.IsFilterSource)
                    {
                        AppliedSourceFilters = JsonSerializer.Deserialize<string[]>(evt.PayloadJson, EventFactory.JsonOptions)
                            ?? throw new ProtocolException("Failed to deserialize string[] of applied source filters.");
                        Logger.LogInformation("Source filter applied: {filter}", string.Join(", ", AppliedSourceFilters));
                    }
                    else if ( evt.IsFilterType )
                    {
                        AppliedTypeFilters = JsonSerializer.Deserialize<EventType[]>(evt.PayloadJson, EventFactory.JsonOptions)
                            ?? throw new ProtocolException("Failed to deserialize string[] of applied type filters.");
                        Logger.LogInformation("Type filter applied: {filter}", string.Join(", ", AppliedTypeFilters.Select(t => t.ToString())));
                    }
                }
                break;
            default:
                return;
        }
    }

    public async Task SetTypeFilterAsync(EventType[] desiredTypes)
    {
        await Task.Run(() => EnqueueTransmit(EventFactory.CreateFilterType(desiredTypes)));
        // wait until filter is confirmed?
    }

    public async Task SetSourceFilterAsync(string[] removedSources)
    {
        await Task.Run(() => EnqueueTransmit(EventFactory.CreateFilterSource(removedSources)));
        // wait until filter is confirmed?
    }

    public void Command<ItemStateType>(string itemName, ItemStateType state) where ItemStateType : struct
    {
        EnqueueTransmit(EventFactory.Command<ItemStateType>(itemName, state));
    }
}
