using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(new EventType[] {
            EventType.ItemStateChangedEvent,
        }, EventTypesMappedAttribute.MappingPriority.Default)
        ]
    public class ItemStateChangedEvent : ItemEvent
    {
        [JsonIgnore]
        public ItemStateChangedEventPayload StateChange
        {
            get => JsonSerializer.Deserialize<ItemStateChangedEventPayload>(PayloadJson)
                ?? throw new ProtocolException($"Failed to deserialize from {nameof(PayloadJson)} = '{PayloadJson}' to {nameof(StateChange)} of type {typeof(ItemStateChangedEventPayload).Name}");
            set { PayloadJson = JsonSerializer.Serialize<ItemStateChangedEventPayload>(value); }
        }

        public ItemStateChangedEvent() : base()
        {
            Type = EventType.ItemStateChangedEvent;
        }

        public override string ToString()
        {
            return $"{{ type: \"{Type}\", item: \"{ItemName}\", payload: {StateChange.ToString()} }}";
        }
    }
}
