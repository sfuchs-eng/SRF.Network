using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using System.Collections.Concurrent;
using MQTTnet.Diagnostics.Logger;

namespace SRF.Network.Mqtt;

public class MqttBrokerConnection : IHostedService, IMqttBrokerConnection, IDisposable
{
    private MqttOptions Config { get; }
    private ILogger<MqttBrokerConnection> Logger { get; }
    private IMqttNetLogger MqttNetLogger { get; }

    public MqttClientFactory MqttClientFactory { get; set; }
    public IMqttClient? Client { get; private set; } = null;

    public bool IsConnected => Client?.IsConnected ?? false;

    /// <summary>
    /// All subscriptions executed.
    /// </summary>
    private List<Subscription> Subscriptions { get; } = [];

    /// <summary>
    /// Queue of pending subscriptions not executed yet.
    /// </summary>
    private BlockingCollection<Subscription> PendingSubscriptions { get; } = [];

    private BlockingCollection<PublishingQueueItem> PublishingQueue { get; } = [];

    private Task? ConnectionRunnerTask { get; set; } = null;
    private Task? SubscriberTask { get; set; } = null;
    private Task? PublisherTask { get; set; } = null;
    private CancellationTokenSource AbortOperations { get; set; } = new CancellationTokenSource();

    public PublishingQueueItem Publish(string topic, string message, EventHandler<PublishEventArgs>? publishedEventHandler = null)
    {
        return Publish(new PublisherString(topic, message), publishedEventHandler);
    }

    public PublishingQueueItem PublishJson<TObject>(string topic, TObject payload, Action<PublisherJson<TObject>>? configure = null, EventHandler<PublishEventArgs>? publishedEventHandler = null) where TObject : class
    {
        var pub = new PublisherJson<TObject>(topic, payload);
        configure?.Invoke(pub);
        return Publish(pub, publishedEventHandler);
    }

    public PublishingQueueItem Publish(IPublisher publisher, EventHandler<PublishEventArgs>? publishedEventHandler = null)
    {
        var pqi = new PublishingQueueItem(publisher);
        if (publishedEventHandler != null)
            pqi.Published += publishedEventHandler;
        PublishingQueue.Add(pqi);
        return pqi;
    }

    private static readonly MqttClientSubscribeResultCode[] SubscriptionOkResultCodes = {
        MqttClientSubscribeResultCode.GrantedQoS0,
        MqttClientSubscribeResultCode.GrantedQoS1,
        MqttClientSubscribeResultCode.GrantedQoS2
    };

    /// <summary>
    /// Queues a subscription request.
    /// It's non-blocking as it only queues the request for delayed / collected execution.
    /// </summary>
    public Subscription Subscribe(string topicPattern, EventHandler<MessageReceivedEventArgs> handleMessageReceived, EventHandler<SubscribedEventArgs>? handleSubscribed = null)
    {
        var subs = new Subscription(topicPattern, handleMessageReceived, handleSubscribed);
        PendingSubscriptions.Add(subs);
        Logger.LogTrace("Subscription to '{mqttTopic}' queued.", topicPattern);
        return subs;
    }

    /// <summary>
    /// Causes the subscription runner to iterate and subscribe to all registered topics.
    /// Yet this class doesn't cause any subscription itself.
    /// </summary>
    private class SubscriptionTrigger : Subscription {
        public SubscriptionTrigger() : base(string.Empty, (sender, args) => { }) { }
    }

    private MqttClientOptions GetMqttClientOptions()
    {
        return new MqttClientOptionsBuilder()
            .WithTcpServer(Config.Host)
            .WithClientId(Config.ClientID)
            /*
            .WithTlsOptions((o) => { o
                .WithAllowUntrustedCertificates()
                .WithCertificateValidationHandler((args) => true)
                .WithIgnoreCertificateChainErrors()
                .WithIgnoreCertificateRevocationErrors()
                })
                */
            .WithCredentials(Config.User, Config.Pass)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(Config.KeepAlifeTime))
            .WithTimeout(TimeSpan.FromSeconds(Math.Max(Config.PingInterval,Config.KeepAlifeTime)*5))
            //.WithoutThrowOnNonSuccessfulConnectResponse()
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (ConnectionRunnerTask != null)
            throw new NotSupportedException("Multiple call of StartAsync is not supported.");

        AbortOperations = new CancellationTokenSource();

        var mqttFactory = MqttClientFactory;
        Client = mqttFactory.CreateMqttClient(MqttNetLogger);
        Client.ConnectedAsync += Client_ConnectedAsync;
        Client.DisconnectedAsync += Client_DisconnectedAsync;

        try
        {
            if (Config.DisableConnection)
            {
                Logger.LogWarning("MQTT connection disabled by config.");
            }
            else
            {
                await TryConnect(cancellationToken);
            }
        }
        catch (MQTTnet.Adapter.MqttConnectingFailedException conFailEx)
        {
            Logger.LogCritical(conFailEx, "Failed to connect to broker. MQTT services not started.");
            Logger.LogTrace("MQTT Host: {mqttHost}, User: {mqttUser}, Pass: {mqttPass}", Config.Host, Config.User, Config.Pass);
            //if ( conFailEx.ResultCode == MqttClientConnectResultCode.)
            return;
        }
        catch ( Exception ex )
        {
            Logger.LogCritical(ex, "Failed to connect to MQTT broker. MQTT services not started.");
            return;
        }

        Client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;

        if (!Config.DisableConnection)
        {
            ConnectionRunnerTask = Task.Run(async () => await ConnectionRunner(AbortOperations.Token), cancellationToken);
            SubscriberTask = Task.Run(async () => await SubscriberRunner(AbortOperations.Token), cancellationToken);
            PublisherTask = Task.Run(async () => await PublishingRunner(AbortOperations.Token), cancellationToken);
        }
    }

    private async Task<bool> TryConnect(CancellationToken cancellationToken)
    {
        var cltOptions = GetMqttClientOptions();
        var conRes = await (Client?.ConnectAsync(cltOptions, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, new CancellationTokenSource(10000).Token).Token)
            ?? throw new MqttException("No client available"));
        Logger.LogTrace("MQTT connect ResultCode: {mqttConnRes}", conRes.ResultCode);
        if (conRes.ResultCode != MqttClientConnectResultCode.Success || !Client.IsConnected)
        {
            Logger.LogWarning("MQTT connection failed! Result code = {mqttConnResultCode}", conRes.ResultCode);
            return false;
        }
        else
        {
            return true;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        synchronizeConnected.Reset();
        AbortOperations.Cancel();
        if (IsConnected)
        {
            await (Client?.DisconnectAsync(reason: MqttClientDisconnectOptionsReason.NormalDisconnection, cancellationToken: cancellationToken) ?? Task.CompletedTask);
        }
    }

    private async Task SubscriberRunner(CancellationToken cancel)
    {
        while (  !PendingSubscriptions.IsCompleted && !cancel.IsCancellationRequested )
        {
            var sub = PendingSubscriptions.Take(cancel); // block until there's one.
            if (cancel.IsCancellationRequested)
                return;
            if (!(sub is SubscriptionTrigger))
                Subscriptions.Add(sub);

            await Task.Delay(TimeSpan.FromSeconds(Config.SubscriptionDelay), cancel);

            while (PendingSubscriptions.Count > 0 && !cancel.IsCancellationRequested)
            {
                sub = PendingSubscriptions.Take(cancel); // non-blocking because there is at least 1 to take.
                if (cancel.IsCancellationRequested)
                    return;
                if (!cancel.IsCancellationRequested && !(sub is SubscriptionTrigger))
                    Subscriptions.Add(sub);
            }

            // subscribe all collected subscriptions
            if ( !(Client?.IsConnected ?? false) )
            {
                Logger.LogDebug("Cannot subscribe as client is not connected.");
                continue;
            }

            var fb = new MqttTopicFilterBuilder();
            try
            {
                var filters = Subscriptions
                        .Select(s => s.TopicPattern)
                        .Distinct()
                        .Select(s => fb.WithTopic(s).Build())
                        .ToList();
                if (filters.Count < 1)
                {
                    Logger.LogTrace("No topic filters for subscription. Skipping subscritpion request.");
                    continue;
                }
                var subsRes = await Client.SubscribeAsync(new MqttClientSubscribeOptions()
                {
                    TopicFilters = filters
                }, cancel);

                var subsResDix = subsRes.Items.ToDictionary(k => k.TopicFilter.Topic, v => v);
                var notificationTasks = Subscriptions
                    .Select(s => new { Subs = s, Res = subsResDix[s.TopicPattern] })
                    .Select(async p =>
                    {
                        try
                        {
                            await p.Subs.NotifySubscribed(Client, p.Res, Logger, cancel);
                        }
                        catch ( Exception ex )
                        {
                            Logger.LogWarning(ex, "Notification about subscription to topic pattern '{mqttTopic}' failed.", p.Subs.TopicPattern);
                        }
                    });
                await Task.WhenAll(notificationTasks);

                var results = subsRes.Items.Select(sr => new { SubsRes = sr, ResultOk = SubscriptionOkResultCodes.Any(okc => okc == sr.ResultCode) });
                foreach (var subsItem in results)
                {
                    if (subsItem.ResultOk)
                        Logger.LogTrace("Subscribed to '{mqttTopic}', result code = {subscriptionResultCode}.", subsItem.SubsRes.TopicFilter.Topic, subsItem.SubsRes.ResultCode);
                    else
                        Logger.LogWarning("MQTT subscription to '{topicPattern}' failed: {subscriptionResultCode}", subsItem.SubsRes.TopicFilter.Topic, subsItem.SubsRes.ResultCode);
                }
            }
            catch ( OperationCanceledException )
            {
                continue;
            }
            catch ( Exception ex )
            {
                Logger.LogWarning(ex, "MQTT subscription failed.");
            }
        }
    }

    private async Task PublishingRunner(CancellationToken cancel)
    {
        while ( (!PublishingQueue.IsCompleted) && (!cancel.IsCancellationRequested) )
        {
            try
            {
                var pub = PublishingQueue.Take(cancel);
                if ( !(Client?.IsConnected ?? false) )
                {
                    // no connection, put item back and wait. Then continue to take and publish.
                    PublishingQueue.Add(pub);
                    Logger.LogWarning("PublishingRunner: client is disconnected. Waiting for connection.");
                    await Task.Delay(TimeSpan.FromSeconds(Config.PublishRetryInterval), cancel);
                    continue;
                }
                await pub.PublishAsync(Client, Logger, cancel);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "MQTT publishing failed.");
            }
        }
    }

    private async Task ConnectionRunner(CancellationToken cancel)
    {
        while ( !cancel.IsCancellationRequested )
        {
            try
            {
                if (!await Client.TryPingAsync(cancel))
                {
                    await TryConnect(cancel);
                }
                else
                {
                    Logger.LogTrace("MQTT ping successful.");
                }
            }
            catch ( Exception e )
            {
                if (e is OperationCanceledException)
                    return;
                Logger.LogWarning(e, "MQTT ping failed with exception.");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(Config.PingInterval), cancel);
            }
        }
    }

    /// <summary>
    /// <code>Set()</code> in <see cref="Client_ConnectedAsync(MqttClientConnectedEventArgs)"/> and
    /// <code>Reset()</code> in <see cref="Client_DisconnectedAsync(MqttClientDisconnectedEventArgs)"/>.
    /// </summary>
    private readonly ManualResetEventSlim synchronizeConnected = new(false);

    /// <summary>
    /// Creates a <see cref="Task"/> that waits for <see cref="synchronizeConnected"/> to be <code>Set()</code>.
    /// </summary>
    /// <returns>The for connected async.</returns>
    /// <param name="cancel">Cancel.</param>
    public async Task WaitUntilConnectedAsync(CancellationToken cancel)
    {
        await Task.Run(() => synchronizeConnected.Wait(cancel), cancel);
    }

    ~MqttBrokerConnection()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool disposed = false;

    public MqttBrokerConnection(
        IOptions<MqttOptions> options,
        ILogger<MqttBrokerConnection> logger,
        IMqttNetLogger mqttNetLogger
    )
    {
        Config = options.Value;
        Logger = logger;
        MqttNetLogger = mqttNetLogger;
        MqttClientFactory = new MqttClientFactory(MqttNetLogger);
    }

    public void Dispose(bool disposing)
    {
        if ( disposed )
            return;
        var clt = Client;
        Client = null;
        clt?.Dispose();
        disposed = true;
    }

    async Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        Logger.LogTrace("MQTT msg received: ContentType = {appMsgContentType}, PayloadAsString = '{appMsgPayload}'", arg.ApplicationMessage.ContentType, arg.ApplicationMessage.ConvertPayloadToString());
        var canceller = CancellationTokenSource.CreateLinkedTokenSource(AbortOperations.Token, new CancellationTokenSource(TimeSpan.FromMinutes(30)).Token);
        var matchingHandlerTasks = Subscriptions
            .Where(s => MqttTopicFilterComparer.Compare(arg.ApplicationMessage.Topic, s.TopicPattern) == MqttTopicFilterCompareResult.IsMatch)
            .Select(s => Task.Run(() =>
            {
                try
                {
                    s.NotifyMessageReceived(this, new MessageReceivedEventArgs(arg));
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "MQTT application message received handler failed. Topic = {mqttTopic}", arg.ApplicationMessage.Topic);
                }
            }, canceller.Token))
            .ToArray();
        await Task.WhenAll(matchingHandlerTasks);
        if (matchingHandlerTasks.Any(t => !t.IsCompleted))
        {
            Logger.LogWarning("MQTT message received: some handlers failed. Topic = {mqttTopic}", arg.ApplicationMessage.Topic);
        }
        arg.IsHandled = true;
    }

    async Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        Logger.LogInformation("MQTT connected event.");
        synchronizeConnected.Set();
        PendingSubscriptions.Add(new SubscriptionTrigger()); // cause subscription of all topics registered & queued.
        await Task.CompletedTask;
    }

    async Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        synchronizeConnected.Reset();
        Logger.LogInformation("MQTT disconnected event.");
        await Task.CompletedTask;
    }
}
