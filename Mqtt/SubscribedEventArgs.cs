using MQTTnet;

namespace SRF.Network.Mqtt;

public class SubscribedEventArgs(Subscription subscription, MqttClientSubscribeResultItem subscriptionResultItem)
{
    public readonly Subscription Subscription = subscription;
    public readonly MqttClientSubscribeResultItem SubscriptionResultItem = subscriptionResultItem;
}
