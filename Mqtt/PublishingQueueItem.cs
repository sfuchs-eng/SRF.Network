using Microsoft.Extensions.Logging;
using MQTTnet;

namespace SRF.Network.Mqtt;

public class PublishingQueueItem(IPublisher publisher)
{
    public IPublisher Publisher { get; } = publisher;
    public MqttClientPublishResult? PublishResult { get; private set; }
    public bool IsPublished { get => PublishResult?.IsSuccess ?? false; }
    public event EventHandler<PublishEventArgs>? Published;

    public async Task PublishAsync(IMqttClient client, ILogger logger, CancellationToken cancel)
    {
        try
        {
            PublishResult = await Publisher.PublishAsync(client, cancel);
            logger.LogTrace("MQTT published to topic '{mqttTopic}'; IsSuccess = {mqttPublishIsSuccess}, ReasonString = {mqttPublishResultString}",
                Publisher.Topic, PublishResult.IsSuccess, PublishResult.ReasonString);
        }
        catch ( Exception ex1 )
        {
            logger.LogWarning(ex1, "MQTT publishing to topic '{mqttTopic}' failed.", Publisher.Topic);
        }

        try
        {
            Published?.Invoke(client, new PublishEventArgs(this));
        }
        catch ( Exception ex )
        {
            logger.LogWarning(ex, "Published event processing failed.");
        }
    }
}
