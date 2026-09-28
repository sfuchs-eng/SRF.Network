using System;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(EventType.GroupStateUpdatedEvent)]
    public class GroupStateUpdatedEvent : Event
    {
        public override IEvent Configure(EventType eventType)
        {
            base.Configure(eventType);
            TopicTokens = new string[] { "openhab", "items", "", "", "stateupdated" };
            return this;
        }

        public GroupStateUpdatedEvent()
        {
            Type = EventType.GroupStateUpdatedEvent;
        }
    }
}
