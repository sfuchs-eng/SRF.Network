using System;
namespace SRF.Network.OpenHab.EventBus
{
    /// <summary>
    /// Which event types acc. the string in <see cref="Event.Type"/> can a class handle?
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class EventTypesMappedAttribute : Attribute
    {
        public EventType[] CompatibleWith { get; private set; } = Array.Empty<EventType>();
        public MappingPriority Priority { get; private set; }

        public EventTypesMappedAttribute(EventType[] typeList, MappingPriority priority = MappingPriority.Default)
        {
            CompatibleWith = typeList;
            Priority = priority;
        }

        public EventTypesMappedAttribute(EventType typeMapped, MappingPriority priority = MappingPriority.Default)
        {
            CompatibleWith = new EventType[] { typeMapped };
            Priority = priority;
        }

        public enum MappingPriority
        {
            Lowest,
            Default,
            High1,
            High2,
            High3,
            Max
        }
    }
}
