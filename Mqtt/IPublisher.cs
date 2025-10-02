using MQTTnet;

namespace SRF.Network.Mqtt;

public interface IPublisher
{
    string Topic { get; }
    PublishingOptions Options { get; set; }
    Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel);
}