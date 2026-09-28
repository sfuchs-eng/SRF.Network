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

    public static EventType[] GetDefaultTypeFilters() =>
    [
        EventType.ItemStateEvent,
        EventType.ItemStateUpdatedEvent,
        EventType.ItemStateChangedEvent,
        EventType.ItemStatePredictedEvent,
        EventType.ItemCommandEvent,
        EventType.ItemAddedEvent,
        EventType.ItemRemovedEvent,
        EventType.ItemUpdatedEvent,
        EventType.GroupStateUpdatedEvent,
        EventType.GroupItemStateChangedEvent
    ];

    protected ArraySegment<byte> Buffer { get; set; }
    //protected JsonDocumentOptions ReceivingJsonOptions { get; set; }

    protected int TransmitPacketCounter { get; set; } = 0;

    public string[] AppliedSourceFilters { get; protected set; } = [];
    public EventType[] AppliedTypeFilters { get; protected set; } = [];
    private string[] DesiredSourceFilters { get; set; } = [];
    private EventType[] DesiredTypeFilters { get; set; } = [];

    private SemaphoreSlim WebSocketReady { get; } = new SemaphoreSlim(0, 1);
    private SemaphoreSlim WebSocketReconnectRequired { get; set; } = new SemaphoreSlim(0, 1);

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
        _ = CloseWithTimeoutAsync(TimeSpan.FromSeconds(5));
    }

    private async Task CloseWithTimeoutAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await CloseAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("OpenHAB websocket close timed out after watchdog timeout.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "OpenHAB websocket close after watchdog timeout failed.");
        }
    }

    protected Uri RequestURI { get => BuildRequestUri(includeAccessToken: true); }
    protected Uri RequestURINoToken { get => BuildRequestUri(includeAccessToken: false); }

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

        Logger.LogInformation("OpenHAB websocket connected successfully to {RequestUri}.", RequestURI);

        // Signal that the WebSocket is ready for receiving/transmitting
        if (WebSocketReady.CurrentCount == 0)
            WebSocketReady.Release();

        // receive, transmit, ... do things until closure
        KeyValuePair<string, Task>[] ptsk = Array.Empty<KeyValuePair<string, Task>>();
        try
        {
            Logger.LogTrace("Starting OpenHAB background workers for websocket lifecycle: reconnect, receive, transmit, notify, watchdog.");
            ptsk = new KeyValuePair<string, Task>[]
            {
                new KeyValuePair<string, Task>(nameof(WebSocketRecreator), WebSocketRecreator(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(ReceivingNotifierAsync), ReceivingNotifierAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(ReceivingLoopAsync), ReceivingLoopAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(TransmittingLoopAsync), TransmittingLoopAsync(StopProcessingTokenSource.Token)),
                new KeyValuePair<string, Task>(nameof(WatchDog.Run), WatchDog.Run(StopProcessingTokenSource.Token))
            };
            foreach (var task in ptsk)
            {
                Logger.LogTrace("Background worker started: {WorkerName}, status={TaskStatus}", task.Key, task.Value.Status);
            }
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

    private Uri BuildRequestUri(bool includeAccessToken = true)
    {
        var uriBuilder = new UriBuilder(Options.WebSocket);
        var path = string.IsNullOrWhiteSpace(uriBuilder.Path) ? "/ws/events" : uriBuilder.Path.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(path) || path == "/")
            path = "/ws/events";
        else if (path.Equals("/ws", StringComparison.OrdinalIgnoreCase))
            path = "/ws/events";

        uriBuilder.Path = path;

        if (!includeAccessToken)
        {
            var existingQuery = uriBuilder.Query.TrimStart('?');
            var filtered = existingQuery
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("accessToken=", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.StartsWith("access_token=", StringComparison.OrdinalIgnoreCase));
            uriBuilder.Query = filtered.Any() ? string.Join("&", filtered) : string.Empty;
            return uriBuilder.Uri;
        }

        var existingQueryWithToken = uriBuilder.Query.TrimStart('?');
        var alreadyHasToken = existingQueryWithToken
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => p.StartsWith("accessToken=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("access_token=", StringComparison.OrdinalIgnoreCase));

        if (!alreadyHasToken && !string.IsNullOrWhiteSpace(Options.AccessToken))
        {
            var escapedAccessToken = Uri.EscapeDataString(Options.AccessToken ?? string.Empty);
            uriBuilder.Query = string.IsNullOrWhiteSpace(existingQueryWithToken)
                ? $"accessToken={escapedAccessToken}"
                : existingQueryWithToken + $"&accessToken={escapedAccessToken}";
        }

        return uriBuilder.Uri;
    }

    private async Task CreateAndConnectWebSocket(CancellationToken cancellationToken)
    {
        var attempts = new[]
        {
            (name: "tokenized", uri: RequestURI),
            (name: "tokenless", uri: RequestURINoToken)
        };

        Exception? lastException = null;
        foreach (var attempt in attempts)
        {
            try
            {
                WSClient = new ClientWebSocket();
                if (Options.AllowInsecureTls)
                    WSClient.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                Logger.LogTrace("Attempting OpenHAB websocket connect to {AttemptUri} ({AttemptName}).", attempt.uri, attempt.name);
                await WSClient.ConnectAsync(attempt.uri, cancellationToken);

                if (!IsWebSocketConnectedByState)
                    continue;

                if (attempt.name == "tokenized" && !string.IsNullOrWhiteSpace(Options.AccessToken)
                    && !attempt.uri.Equals(RequestURINoToken))
                {
                    Logger.LogInformation("OpenHAB websocket connected successfully using tokenized URL at {AttemptUri}.", attempt.uri);
                }

                ApplyPersistentFiltersAfterConnect();
                return;
            }
            catch (OperationCanceledException oce)
            {
                IsActive = false;
                throw new ConnectionException("Connecting cancelled.", oce);
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.LogWarning(ex, "Failed to connect to '{AttemptUri}' using websocket attempt '{AttemptName}'. Trying fallback only if available.", attempt.uri, attempt.name);
            }
        }

        IsActive = false;
        Logger.LogError(lastException, "Failed to connect to OpenHAB server at {ServerURI} with both tokenized and tokenless websocket attempts.", Options.WebSocket);
        throw new ConnectionException("Connecting failed.", lastException ?? new InvalidOperationException("WebSocket connect failed without an exception."));
    }

    private void ApplyPersistentFiltersAfterConnect()
    {
        // Re-apply persisted type/source filters after each websocket (re)connect.
        // openHAB filter state is connection-scoped and therefore must be restored.
        if (DesiredTypeFilters.Length > 0)
        {
            Logger.LogTrace("Re-applying OpenHAB websocket type filter after connect: {types}",
                string.Join(", ", DesiredTypeFilters.Select(t => t.ToString())));
            _ = SetTypeFilterAsync(DesiredTypeFilters);
        }
        else
        {
            var defaultItemTypes = GetDefaultTypeFilters();
            Logger.LogTrace("Applying default OpenHAB item-event type filter: {types}",
                string.Join(", ", defaultItemTypes.Select(t => t.ToString())));
            _ = SetTypeFilterAsync(defaultItemTypes);
        }

        if (DesiredSourceFilters.Length > 0)
        {
            Logger.LogTrace("Re-applying OpenHAB websocket source filter after connect: {sources}",
                string.Join(", ", DesiredSourceFilters));
            _ = SetSourceFilterAsync(DesiredSourceFilters);
            return;
        }

        if (Options.FilterSource)
        {
            Logger.LogTrace("Applying default OpenHAB source filter to only receive events from source {source}.", Options.SourceEntity);
            _ = SetSourceFilterAsync(new[] { Options.SourceEntity });
            return;
        }

        Logger.LogDebug("OpenHAB source filtering is disabled; accepting all item events from the server.");
    }

    private void TriggerWebSocketRecreationAndReconnection()
    {
        if (WebSocketReady.CurrentCount > 0)
            WebSocketReady.Wait(0);
        if (WebSocketReconnectRequired.CurrentCount == 0)
            WebSocketReconnectRequired.Release();
    }

    private async Task WebSocketRecreator(CancellationToken cancellationToken)
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            try
            {
                await WebSocketReconnectRequired.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

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
                if (WebSocketReconnectRequired.CurrentCount > 0)
                    WebSocketReconnectRequired.Wait(0);
                if (WebSocketReady.CurrentCount == 0)
                    WebSocketReady.Release();
            }
            catch ( Exception e2)
            {
                Logger.LogError(e2, "Failed to recreate WebSocket or reconnect. Waiting and retrying...");
                await Task.Delay(Options.ReconnectWaitTime, cancellationToken);
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
                            Logger.LogTrace("Notifier dequeued event: type={eventType}, topic={topic}", cur.Received.Type, cur.Received.Topic);
                            Logger.LogDebug("OpenHAB event received: {IEvent}", cur.Received.ToString());
                            EventReceived?.Invoke(this, cur);
                            Logger.LogTrace("Notifier invoked subscribers for event type={eventType}, topic={topic}", cur.Received.Type, cur.Received.Topic);
                        }
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning("Processing of received IEvent {eventText} failed with exception {exceptionType}: {exceptionMessage}", cur?.ToString(), e.GetType().Name, e.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Logger.LogTrace("{funcName} canceled.", nameof(ReceivingNotifierAsync));
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
                while ( WebSocketReady.CurrentCount == 0 )
                {
                    await Task.Delay(100, cancellation);
                }

                if (WSClient?.State != WebSocketState.Open)
                {
                    Logger.LogWarning("Websocket not in state Open. Requesting reconnection.");
                    TriggerWebSocketRecreationAndReconnection();
                    continue;
                }

                try
                {
                    Logger.LogTrace("ReceivingLoopAsync before ReceiveAsync: websocketState={webSocketState}, queueCompleted={queueCompleted}", WSClient?.State, ReceivingQueue.IsCompleted);
                    var evt = await ReceiveAsync(cancellation);
                    Logger.LogTrace("ReceiveAsync returned event type={eventType}, clrType={clrType}", evt?.Type, evt?.GetType().Name ?? "<null>");
                    if (ReceivingQueue.IsCompleted || cancellation.IsCancellationRequested)
                        break;
                    if (evt == null)
                        continue;
                    if (evt.Type == EventType.WebSocketEvent && evt is WebSocketEvent wse && wse.IsResponseFailed)
                        Logger.LogError("Failure response: {evt}", wse.ToString());
                    ReceivingQueue.Add(new EventReceivedEventArgs(evt, _timeProvider.GetUtcNow()));
                    Logger.LogTrace("Queued event for notifier: type={eventType}, topic={topic}", evt.Type, evt.Topic);
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
        TrackDesiredFilters(sendEvent);
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
            await Task.Delay(1000, token);

            Logger.LogTrace("Starting transmitting loop...");
            while ( !SendingQueue.IsCompleted && !token.IsCancellationRequested )
            {
                // check & wait until websocket ready
                while (WSClient?.State != WebSocketState.Open && !token.IsCancellationRequested)
                {
                    while (WebSocketReady.CurrentCount == 0 && !token.IsCancellationRequested)
                    {
                        // assume websocket is being connected, wait and try again.
                        await Task.Delay(1000, token);
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
            Log.Clients.WaitingForNextWebSocketFrame(Logger, RequestURI, WSClient?.State);
            res = await (WSClient?.ReceiveAsync(Buffer, cts.Token)
                ?? throw new ConnectionException("No websocket object for receiving."));
            Log.Clients.WebSocketFrameReceived(Logger, res.Count, res.EndOfMessage, res.MessageType, WSClient?.State);
            if (res.Count > 0)
                await message.WriteAsync(Buffer.Array ?? throw new ConnectionException("Failed to get buffer array for receiving."), Buffer.Offset, res.Count, cts.Token);
        } while (!(res?.EndOfMessage ?? true || cancellationToken.IsCancellationRequested ));

        cancellationToken.ThrowIfCancellationRequested();

        return EventFactory.Create(message);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Logger.LogTrace("Closing connection...");
        StopProcessingTokenSource.Cancel();

        if (!SendingQueue.IsAddingCompleted)
            SendingQueue.CompleteAdding();
        if (!ReceivingQueue.IsAddingCompleted)
            ReceivingQueue.CompleteAdding();

        var currentTaskId = Task.CurrentId;
        var tasksToAwait = currentTaskId.HasValue
            ? ProcessingTasks.Where(task => task.Id != currentTaskId.Value).ToArray()
            : ProcessingTasks.ToArray();

        try
        {
            await Task.WhenAll(tasksToAwait);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("One or more OpenHAB background tasks canceled during shutdown.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "One or more OpenHAB background tasks failed during shutdown.");
        }

        ProcessingTasks = new List<Task>(10);

        try
        {
            await (WSClient?.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Regular closure", cancellationToken)
                ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("OpenHAB websocket close was canceled during shutdown.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "OpenHAB websocket close failed during shutdown.");
        }

        Logger.LogTrace("Connection closed.");
    }

    public async Task SendAsync(IEvent sendEvent, CancellationToken cancellationToken)
    {
        TrackDesiredFilters(sendEvent);

        var mcts = CancellationTokenSource.CreateLinkedTokenSource(StopProcessingTokenSource.Token, cancellationToken);
        sendEvent.ID = (++TransmitPacketCounter).ToString();
        sendEvent.Source = Options.SourceEntity;
        var serEvt = EventFactory.Serialize(sendEvent);

        Log.Clients.TransmittingEvent(Logger, sendEvent.GetType().Name, System.Text.Encoding.UTF8.GetString([.. serEvt]));

        await (WSClient?.SendAsync(
            serEvt,
            WebSocketMessageType.Text,
            true, mcts.Token
            ) ?? throw new ConnectionException($"No websocket object for sending {nameof(IEvent)}."));
    }

    private void TrackDesiredFilters(IEvent sendEvent)
    {
        if (sendEvent is not WebSocketEvent ws)
            return;

        if (ws.IsFilterType)
        {
            var typeNames = JsonSerializer.Deserialize<string[]>(ws.PayloadJson, EventFactory.JsonOptions) ?? [];
            var parsedTypes = typeNames
                .Select(typeName => Enum.TryParse(typeName, out EventType parsed) ? parsed : EventType.Unrecognized)
                .Where(t => t is not EventType.Unrecognized and not EventType.Undefined)
                .Distinct()
                .ToArray();

            if (parsedTypes.Length > 0)
                DesiredTypeFilters = parsedTypes;
        }
        else if (ws.IsFilterSource)
        {
            DesiredSourceFilters = JsonSerializer.Deserialize<string[]>(ws.PayloadJson, EventFactory.JsonOptions) ?? [];
        }
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
                        Logger.LogTrace("Source filter applied: {filter}", string.Join(", ", AppliedSourceFilters));
                    }
                    else if ( evt.IsFilterType )
                    {
                        AppliedTypeFilters = JsonSerializer.Deserialize<EventType[]>(evt.PayloadJson, EventFactory.JsonOptions)
                            ?? throw new ProtocolException("Failed to deserialize string[] of applied type filters.");
                        Logger.LogTrace("Type filter applied: {filter}", string.Join(", ", AppliedTypeFilters.Select(t => t.ToString())));
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
