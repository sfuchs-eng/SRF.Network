using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SRF.Network.OpenHab.Client;
using UnitsNet;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(new EventType[] {
            EventType.ItemStateEvent,
            EventType.ItemCommandEvent,
        }, EventTypesMappedAttribute.MappingPriority.Default)
        ]
    public class ItemEventTypeValue : ItemEvent
    {
        public override IEvent Configure(EventType eventType)
        {
            base.Configure(eventType);
            TopicTokens[3] = eventType switch
            {
                EventType.ItemStateEvent => "state",
                EventType.ItemCommandEvent => "command",
                _ => throw new NotImplementedException($"Beef up {nameof(ItemEventTypeValue)} to support EventType {eventType} in Configure(EventType)."),
            };
            return this;
        }

        [JsonIgnore]
        public TypeValuePayload State {
            get => JsonSerializer.Deserialize<TypeValuePayload>(PayloadJson)
                ?? throw new ProtocolException($"Failed to deserialize from {nameof(PayloadJson)} = '{PayloadJson}' to {nameof(State)} of type {typeof(TypeValuePayload).Name}");
            set { PayloadJson = JsonSerializer.Serialize<TypeValuePayload>(value); }
        }

        public ItemEventTypeValue() :base()
        {
            Type = EventType.ItemStateEvent;
        }

        public ItemEventTypeValue Set(IQuantity quantity)
        {
            State = new TypeValuePayload().Set(quantity);
            return this;
        }

        public ItemEventTypeValue Set(int number)
        {
            State = new TypeValuePayload().Set<int>(number);
            return this;
        }

        public ItemEventTypeValue Set(double number)
        {
            State = new TypeValuePayload().Set<double>(number);
            return this;
        }

        public ItemEventTypeValue Set(ItemStateSwitch state)
        {
            State = new TypeValuePayload().Set<ItemStateSwitch>(state);
            return this;
        }

        public ItemEventTypeValue Set(ItemStateContact state)
        {
            State = new TypeValuePayload().Set(state);
            return this;
        }

        public ItemEventTypeValue Set<T>(T state) where T : struct
        {
            State = new TypeValuePayload().Set<T>(state);
            return this;
        }

        public IQuantity GetQuantity(System.Type quantityType) => Quantity.Parse(quantityType, State.Value);
        public int GetInt() => int.Parse(State.Value);
        public double GetDouble() => double.Parse(State.Value);
        public bool OnOffIsOn() => State.IsTypeSwitch() ? State.IsOn() : throw new ArgumentException($"OnOffIsOn requires OnOff value type instead of '{State.Type}'");
        public bool OpenClosedIsOpen() => State.IsTypeContact() ? State.IsOpen() : throw new ArgumentException($"OpenClosedIsOpen requires OpenClosed value type instead of '{State.Type}'");
    }
}
