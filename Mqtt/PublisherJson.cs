using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;

namespace SRF.Network.Mqtt;

public class PublisherJson<TObject>(string topic, TObject payload) : IPublisher where TObject: class
{
    public string Topic { get; } = topic;
    public TObject Payload { get; } = payload;

    public PublishingOptions Options { get; set; } = new();

    public async Task<MqttClientPublishResult> PublishAsync(IMqttClient client, CancellationToken cancel)
    {
        return await client.PublishBinaryAsync(
            topic: Topic,
            payload: JsonSerializer.SerializeToUtf8Bytes<TObject>(Payload, Options.JsonOptions),
            qualityOfServiceLevel: Options.ServiceLevel,
            retain: Options.Retain,
            cancellationToken: cancel
        );
    }
}
