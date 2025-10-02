using System.Text.Json;
using MQTTnet.Protocol;

namespace SRF.Network.Mqtt;

public class PublishingOptions
{
    public MqttQualityOfServiceLevel ServiceLevel { get; set; } = MqttQualityOfServiceLevel.ExactlyOnce;
    public bool Retain { get; set; } = false;
    public JsonSerializerOptions? JsonOptions { get; set; } = null;
}
