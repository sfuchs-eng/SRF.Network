using System;
using System.Text.Json.Serialization;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(EventType.ItemStateUpdatedEvent)]
    public class ItemStateUpdatedEvent : ItemEventTypeValue
    {
        public override IEvent Configure(EventType eventType)
        {
            base.Configure(eventType);
            TopicTokens[3] = "stateupdated";
            return this;
        }

        public ItemStateUpdatedEvent() : base()
        {
            Type = EventType.ItemStateUpdatedEvent;
        }
    }
}
