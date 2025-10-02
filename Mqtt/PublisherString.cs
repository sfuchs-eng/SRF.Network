using MQTTnet;
using MQTTnet.Protocol;

namespace SRF.Network.Mqtt;

public class PublisherString(string topic, string payload) : IPublisher
{
    public string Topic { get; } = topic;
    public string Payload { get; } = payload;
    public PublishingOptions Options { get; set; } = new();

    public async Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel)
    {
        return await client.PublishStringAsync(
            topic: Topic,
            payload: Payload,
            qualityOfServiceLevel: Options.ServiceLevel,
            retain: Options.Retain,
            cancellationToken: cancel
        );
    }
}
