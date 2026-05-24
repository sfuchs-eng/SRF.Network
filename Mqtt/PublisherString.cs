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
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(Topic)
            .WithPayload(Payload)
            .WithQualityOfServiceLevel(Options.ServiceLevel)
            .WithRetainFlag(Options.Retain)
            .Build();

        if (!string.IsNullOrWhiteSpace(Options.ContentType))
            message.ContentType = Options.ContentType;

        return await client.PublishAsync(message, cancel);
    }
}
