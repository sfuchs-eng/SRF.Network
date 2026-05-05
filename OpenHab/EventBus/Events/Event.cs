using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SRF.Network.OpenHab.EventBus
{

    // JSON serialization: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom
    /* use with inheriting types:
    [EventTypesMapped(new EventType[] {
            EventType.,
            EventType.,
        }, EventTypesMappedAttribute.MappingPriority.Lowest)
        ]
    */
    /// <summary>
    /// Use the <see cref="EventTypesMappedAttribute"/> on inheriting classes
    /// to control the <see cref="EventFactory"/>s object instanciation mechanism.
    /// </summary>
    public abstract class Event : IEvent
    {
        [JsonPropertyName("type")]
        [JsonRequired]
        public virtual EventType Type { get; set; } = EventType.Undefined;

        [JsonIgnore]
        public virtual string[] TopicTokens { get; set; } = Array.Empty<string>();

        [JsonPropertyName("topic")]
        [JsonRequired]
        public virtual string Topic
        {
            get
            {
                return string.Join("/", TopicTokens);
            }
            set
            {
                TopicTokens = value.Split('/');
            }
        }

        private string _payloadJson = String.Empty;

        /// <summary>
        /// Override in inheriting classes to serialize/deserialize payload.
        /// </summary>
        [JsonPropertyName("payload")]
        [JsonRequired]
        public virtual string PayloadJson {
            get
            {
                return _payloadJson;
            }
            set
            {
                _payloadJson = value;
            }
        }

        [JsonPropertyName("eventId")]
        public virtual string? ID { get; set; }

        [JsonPropertyName("source")]
        public virtual string? Source { get; set; }

        private static JsonSerializerOptions? jsonOptions;

        public override string ToString()
        {
            if (jsonOptions == null)
            {
                jsonOptions = EventFactory.DefaultJsonSerializerOptions;
            }
            return JsonSerializer.Serialize(this, this.GetType(), jsonOptions);
        }

        /// <summary>
        /// Sets the <see cref="Type"/> property.
        /// Ensure to set the <see cref="TopicTokens"/> in inheriting classes' overrides prior calling <code>base.Configure()</code>.
        /// Throws an <see cref="EventException"/> in case the topic tokens are not all set.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        public virtual IEvent Configure(EventType eventType)
        {
            Type = eventType;
            if (TopicTokens.Length < 2)
                throw new EventException($"{this.GetType().FullName}: Ensure setting TopicTokens prior calling Event.Configure() via base.Configure().");
            TopicTokens[0] = "openhab";
            return this;
        }
    }

    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        UseStringEnumConverter = true,
        AllowOutOfOrderMetadataProperties = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    )]
    [JsonSerializable(typeof(Event))]
    internal partial class EventMetadataOnlyContext : JsonSerializerContext
    {
    }
}
