using Microsoft.Extensions.Logging;
using MQTTnet.Client;

namespace SRF.Network.Mqtt;

public class Subscription
{
    public string TopicPattern { get; }
    public event EventHandler<MessageReceivedEventArgs>? MessageReceivedHandler;
    public event EventHandler<SubscribedEventArgs>? Subscribed;

    public Subscription(string topicPattern, EventHandler<MessageReceivedEventArgs> messageReceivedHandler, EventHandler<SubscribedEventArgs>? handleSubscribed = null)
    {
        TopicPattern = topicPattern;
        MessageReceivedHandler += messageReceivedHandler;
        if (handleSubscribed != null)
            Subscribed += handleSubscribed;
    }

    public async Task NotifySubscribed(IMqttClient client, MqttClientSubscribeResultItem subscriptionResultItem, ILogger logger, CancellationToken cancel)
    {
        try
        {
            if (Subscribed != null)
                await Task.Run(() => Subscribed.Invoke(client, new SubscribedEventArgs(this, subscriptionResultItem)), cancel);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subscribed event processing for topic '{mqttTopicFilter}' failed.", TopicPattern);
        }
    }

    internal void NotifyMessageReceived(MqttBrokerConnection client, MessageReceivedEventArgs args)
    {
        MessageReceivedHandler?.Invoke(client, args);
    }
}
