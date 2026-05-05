using System;
using System.Text.Json.Serialization;

namespace SRF.Network.OpenHab.EventBus.Events
{
    public class ItemStateChangedEventPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = String.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = String.Empty;

        [JsonPropertyName("oldType")]
        public string OldType { get; set; } = String.Empty;

        [JsonPropertyName("oldValue")]
        public string OldValue { get; set; } = String.Empty;

        public override string ToString()
        {
            return $": {OldValue} --> {Value} (Type: {OldType} --> {Type})";
        }

        [JsonIgnore]
        public TypeValuePayload Current { get => new TypeValuePayload() { Type = Type, Value = Value }; }

        [JsonIgnore]
        public TypeValuePayload Old { get => new TypeValuePayload() { Type = OldType, Value = OldValue }; }
    }
}
