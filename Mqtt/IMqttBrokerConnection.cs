using Microsoft.Extensions.Hosting;
using MQTTnet;

namespace SRF.Network.Mqtt;

/// <summary>
/// Abstraction of a client connection to an MQTT broker.
/// Use <see cref="Hosting.MqttHostingExtensions.AddMqtt(Microsoft.Extensions.DependencyInjection.IServiceCollection, string?)"/> or similar methods
/// to get the <see cref="MqttBrokerConnection"/> implementation of this interface injected into your services including
/// a background service managing the connection and the publishing queue as well as the options <see cref="MqttOptions"/>.
/// </summary> <summary>
public interface IMqttBrokerConnection : IHostedService, IDisposable
{
    IMqttClient? Client { get; }

    bool IsConnected { get; }

    /// <summary>
    /// Yield a task running on the thread pool that blocks until another thread has (re)connected.
    /// </summary>
    Task WaitUntilConnectedAsync(CancellationToken cancel);

    /// <summary>
    /// Enqueues a <see cref="PublisherString"/>.
    /// Configures <paramref name="publishedEventHandler"/> if != null into the returned <see cref="PublishingQueueItem"/> prior putting it onto the publishing queue.
    /// </summary>
    PublishingQueueItem Publish(string topic, string message, EventHandler<PublishEventArgs>? publishedEventHandler = null);

    /// <summary>
    /// Puts the <paramref name="publisher"/> into the publishing queue.
    /// Configures <paramref name="publishedEventHandler"/> if != null into the returned <see cref="PublishingQueueItem"/> prior putting it onto the publishing queue.
    /// </summary>
    PublishingQueueItem Publish(IPublisher publisher, EventHandler<PublishEventArgs>? publishedEventHandler = null);

    /// <summary>
    /// Enqueues a <see cref="PublisherJson{TObject}"/> for publishing <paramref name="payload"/> in JSON format.
    /// </summary>
    PublishingQueueItem PublishJson<TObject>(string topic, TObject payload, Action<PublisherJson<TObject>>? configure = null, EventHandler<PublishEventArgs>? publishedEventHandler = null) where TObject : class;

    /// <summary>
    /// Registers a topic <paramref name="topicPattern"/> (filter) for subscription.
    /// The subscription itself may take place delayed, collecting multiple topic subscriptions on beforehand.
    /// </summary>
    /// <param name="topicPattern">topic filter pattern</param>
    /// <param name="handleMessageReceived">application message received handler. Is only called in case the topic matches the given filter pattern.</param>
    Subscription Subscribe(string topicPattern, EventHandler<MessageReceivedEventArgs> handleMessageReceived, EventHandler<SubscribedEventArgs>? handleSubscribed = null);
}
