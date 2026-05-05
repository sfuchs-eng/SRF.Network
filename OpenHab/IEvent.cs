using System;
namespace SRF.Network.OpenHab
{
    public interface IEvent
    {
        /// <summary>
        /// type
        /// Event type acc. <see cref="EventBus.EventType"/>.
        /// Allows handling different event types by the same .NET class.
        /// </summary>
        EventBus.EventType Type { get; }

        /// <summary>
        /// Configures the specified <see cref="EventBus.EventType"/> for the object. Initializes internals such as e.g. <see cref="Type"/> and <see cref="Topic"/>.
        /// May be called several times, in which case the object type and all dependent information shall be reinitialized.
        /// May be called not at all, e.g. in case the object is instanciated via JSON deserialization.
        /// May throw an <see cref="EventBus.EventException"/> in case theres a mismatch between <paramref name="eventType"/> and the implementing class type.
        /// </summary>
        /// <param name="eventType">event type ID</param>
        IEvent Configure(EventBus.EventType eventType);

        /// <summary>
        /// topic
        /// OpenHAB event bus topic. Non-null
        /// </summary>
        string Topic { get; }

        /// <summary>
        /// payload
        /// JSON encoded payload. Non-null
        /// </summary>
        string PayloadJson { get; }

        /// <summary>
        /// eventID
        /// Optional (nullable), gets copied into the response message in case of an error.
        /// </summary>
        string? ID { get; set; }

        /// <summary>
        /// source
        /// Optional (nullable)
        /// </summary>
        string? Source { get; set; }
    }
}
