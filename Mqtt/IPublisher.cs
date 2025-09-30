using MQTTnet;

namespace SRF.Network.Mqtt;

public interface IPublisher
{
    string Topic { get; }
    Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel);
}