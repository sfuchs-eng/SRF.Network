using MQTTnet;
using MQTTnet.Protocol;

namespace SRF.Network.Mqtt;

public class PublisherString(string topic, string payload) : IPublisher
{
    public string Topic { get; } = topic;
    public string Payload { get; } = payload;
    public MqttQualityOfServiceLevel ServiceLevel { get; set; } = MqttQualityOfServiceLevel.ExactlyOnce;
    public bool Retain { get; set; } = false;

    public async Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel)
    {
        return await client.PublishStringAsync(
            topic: Topic,
            payload: Payload,
            qualityOfServiceLevel: ServiceLevel,
            retain: Retain,
            cancellationToken: cancel
        );
    }
}
