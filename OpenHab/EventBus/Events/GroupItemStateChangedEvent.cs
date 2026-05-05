using System;
namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(EventType.GroupItemStateChangedEvent)]
    public class GroupItemStateChangedEvent : ItemEvent
    {
        public override IEvent Configure(EventType eventType)
        {
            base.Configure(eventType);
            return this;
        }
    }
}
