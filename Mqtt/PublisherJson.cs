using System.Text.Json;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace SRF.Network.Mqtt;

public class PublisherJson<TObject> : IPublisher where TObject: class
{
    public string Topic { get; }
    public TObject Payload { get; }
    public JsonSerializerOptions? JsonOptions { get; set; } = null;
    public MqttQualityOfServiceLevel ServiceLevel { get; set; } = MqttQualityOfServiceLevel.ExactlyOnce;
    public bool Retain { get; set; } = false;

    public PublisherJson(string topic, TObject payload)
    {
        Topic = topic;
        Payload = payload;
    }

    public async Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel)
    {
        return await client.PublishBinaryAsync(
            topic: Topic,
            payload: JsonSerializer.SerializeToUtf8Bytes<TObject>(Payload, JsonOptions),
            qualityOfServiceLevel: ServiceLevel,
            retain: Retain,
            cancellationToken: cancel
        );
    }
}
