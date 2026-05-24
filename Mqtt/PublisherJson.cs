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
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(Topic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes<TObject>(Payload, Options.JsonOptions))
            .WithQualityOfServiceLevel(Options.ServiceLevel)
            .WithRetainFlag(Options.Retain)
            .Build();

        if (!string.IsNullOrWhiteSpace(Options.ContentType))
            message.ContentType = Options.ContentType;

        return await client.PublishAsync(message, cancel);
    }
}
