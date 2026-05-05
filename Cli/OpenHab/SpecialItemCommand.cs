using System.Text.Json.Serialization;
using SRF.Network.OpenHab.EventBus;

namespace SRF.Network.Cli.OpenHab;

public class SpecialItemCommand
{
    [JsonPropertyName("type")]
    public EventType Type { get; set; }

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    //public TypeValuePayload Payload { get; set; }
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string ID { get; set; } = "1";

    [JsonPropertyName("source")]
    public string Source { get; set; } = typeof(SpecialItemCommand).FullName ?? "SpecialItemCommand";
}
