using System;
namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(new EventType[] {
            EventType.ThingAddedEvent,
            EventType.ThingRemovedEvent,
            EventType.ThingUpdatedEvent,
            EventType.ThingStatusInfoEvent,
            EventType.ThingStatusInfoChangedEvent,
        }, EventTypesMappedAttribute.MappingPriority.Lowest)
        ]
    public class ThingEvent : Event
    {
    }
}
