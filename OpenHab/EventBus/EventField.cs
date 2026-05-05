using System;
namespace SRF.Network.OpenHab.EventBus
{
    internal enum EventField
    {
        // case matters
        type,
        topic,
        payload,
        eventId,
        source,
    }

    internal static class EventFieldHelper
    {
        public static string GetJsonPropertyName(this EventField field)
        {
            return field.ToString();
        }
    }
}
