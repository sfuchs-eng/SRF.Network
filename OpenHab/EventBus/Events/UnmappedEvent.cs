using System;
namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(
        new EventType[]
        {
            EventType.Undefined,
            EventType.Unrecognized,

            EventType.RuleAddedEvent,
            EventType.RuleRemovedEvent,
            EventType.RuleStatusInfoEvent,
        },
        EventTypesMappedAttribute.MappingPriority.Lowest)
    ]
    public class UnmappedEvent : Event
    {
    }
}
