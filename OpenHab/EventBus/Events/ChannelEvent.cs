using System;
namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(new EventType[] {
            EventType.ChannelTriggeredEvent,
            EventType.ChannelDescriptionChangedEvent,
        }, EventTypesMappedAttribute.MappingPriority.Default)
        ]
    public class ChannelEvent : Event
    {
    }
}
