using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using System.Reflection;
using SRF.Network.OpenHab.EventBus.Events;
using System.Text.Encodings.Web;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.OpenHab.EventBus
{
    public class EventFactory : IEventFactory
    {
        public EventFactory(ILogger<EventFactory> logger)
        {
            logger.LogDebug("Initializing...");
            Logger = logger;
            JsonOptions = DefaultJsonSerializerOptions; // via IOptions and host config doesn't make sense as it's at least partly protocal bound.
            EventTypeMap = BuildEventTypeMap();
        }

        public ILogger Logger { get; private set; }

        public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new JsonSerializerOptions()
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, /* we need the \" style encoding */
            Converters = { new JsonStringEnumConverter() },
        };

        public JsonSerializerOptions JsonOptions { get; set; }

        public static readonly JsonDocumentOptions DefaultJsonDocumentOptions = new JsonDocumentOptions()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        public JsonDocumentOptions JsonDocOptions { get; set; } = DefaultJsonDocumentOptions;

        /// <summary>
        /// Maps Json property <see cref="IEvent.Type"/> value to implementing classes.
        /// </summary>
        public Dictionary<EventType, Type> EventTypeMap { get; set; } = new Dictionary<EventType, Type>();

        public Type DefaultEventType { get; set; } = typeof(UnmappedEvent);

        protected Dictionary<EventType,Type> BuildEventTypeMap()
        {
            // List of types by EventType enum value.
            var etCand = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IEvent).IsAssignableFrom(t))
                .Select(t => new { EvtType = t, MapMeta1 = t.GetCustomAttribute<EventTypesMappedAttribute>() })
                .Where(tm => tm.EvtType != null && tm.MapMeta1 != null)
                .SelectMany(tm => tm.MapMeta1!
                    .CompatibleWith.Select(id => new { EType = tm.EvtType, MapMeta = tm.MapMeta1, ID = id })
                )
                .GroupBy(etTripple => etTripple.ID)
                .ToArray(); // complete list of all candiate Classes with mapped EventType IDs

            var et = etCand.Select(idTripplesGroup => idTripplesGroup
                    .OrderBy(p => p.MapMeta.Priority)
                    .FirstOrDefault()) // select prioritized Type per EventType
                .Where(k => k != null)
                .ToDictionary(k => k!.ID, v => v!.EType);

            Logger.LogDebug("Mapped event types: [{mapList}]", string.Join(", ", et.Select(p => $"{{ {p.Key}: {p.Value.FullName} }}")));
            var mappedCnt = et.Count;
            var expectedCnt = Enum.GetValues(typeof(EventType)).Length;
            if (mappedCnt != expectedCnt)
            {
                var missing = ((EventType[])Enum.GetValues(typeof(EventType))).Where(t => !et.ContainsKey(t));
                Logger.LogWarning("Only {mappedCnt} EventTypes mapped out of {enlistedCnt} available types. Miss mapping to an IEvent class: {missingList}",
                    mappedCnt, expectedCnt, string.Join(", ", missing));
            }
            return et;
        }

        private Type GetEventType(EventType typeID)
        {
            //Logger.LogTrace("{Function}: mapping {typeID}", nameof(GetEventType), typeID);
            if (!EventTypeMap.TryGetValue(typeID, out Type? eventType))
            {
                eventType = DefaultEventType;
                Logger.LogWarning("No class for event type ID {eventTypeID} in map. Using {eventTypeName}", typeID, eventType.FullName);
            }
            else
                Logger.LogTrace("EventType mapped: {typeID} --> {className}", typeID, eventType?.FullName ?? "-null,failed-");
            return eventType ?? throw new ProtocolException($"Failed to determine matching Type for type ID {typeID}");
        }

        private EventType GetEventTypeId(JsonDocument message)
        {
            var typeElement = message.RootElement.GetProperty(EventField.type.GetJsonPropertyName());
            if (typeElement.ValueKind == JsonValueKind.String)
            {
                var eventTypeJsonName = typeElement.GetString()
                    ?? throw new EventParsingException("Cannot get 'type' property from json data. Unable to identify event type.");
                if (Enum.TryParse(eventTypeJsonName, out EventType eventTypeID))
                    return eventTypeID;

                Logger.LogDebug("Failed to parse json event type {eventTypeJsonName} to enum EventType value.", eventTypeJsonName);
                throw new EventParsingException($"Unknown OpenHAB event type '{eventTypeJsonName}'.");
            }

            if (typeElement.ValueKind == JsonValueKind.Number && typeElement.TryGetInt32(out var numericEventType))
            {
                if (Enum.IsDefined(typeof(EventType), numericEventType))
                    return (EventType)numericEventType;

                throw new EventParsingException($"Unknown OpenHAB event type ID '{numericEventType}'.");
            }

            throw new EventParsingException("Cannot get 'type' property from json data. Unable to identify event type.");
        }

        private Type GetEventType(JsonDocument message)
        {
            return GetEventType(GetEventTypeId(message));
        }

        public IEvent Create(EventType eventTypeID)
        {
            //Logger.LogTrace("Creating IEvent for {typeID}", eventTypeID);
            var evt = (IEvent)(Activator.CreateInstance(GetEventType(eventTypeID))
                ?? throw new ProtocolException($"IEvent instanciation for type ID {eventTypeID} failed."));
            evt.Configure(eventTypeID);
            //Logger.LogTrace("Event created, type {evtType}: {evtContent}", evt?.GetType().Name, evt?.ToString());
            return evt;
        }

        public IEvent Create(JsonDocument message)
        {
            // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to?pivots=dotnet-6-0#support-polymorphic-deserialization
            IEvent evt;
            try
            {
                var eventTypeId = GetEventTypeId(message);
                var eventType = GetEventType(eventTypeId);
                evt = (IEvent)(message.Deserialize(eventType, JsonOptions)
                    ?? throw new ProtocolException("Deserialize returned null without exception."));
            }
            catch (Exception ex)
            {
                string? payload = null;
                try { payload = JsonSerializer.Serialize(message); }
                catch { payload = "<message?, JsonDoc serialization failed>"; }
                Logger.LogDebug(ex, "IEvent deserialization failed. JsonDocument serialized: '{jsonDoc}'", payload);
                throw new EventParsingException($"Payload failed to deserialize: {payload}", ex);
            }
            return evt;
        }

        public IEvent Create(MemoryStream jsonPayload)
        {
            try
            {
                jsonPayload.Position = 0;
                JsonDocument jd = JsonDocument.Parse(jsonPayload, JsonDocOptions);
                Logger.LogTrace("Rx event, raw payload: {packetPayload}", System.Text.Encoding.UTF8.GetString(jsonPayload.ToArray()));
                return Create(jd);
            }
            catch (Exception ex)
            {
                jsonPayload.Position = 0;
                // debug only, events are extensible and many types are not supported. So Exceptions here are quite frequent.
                Logger.LogDebug(ex, "OpenHAB event parsing failed. Raw message ({length} bytes): '{eventRaw}'", jsonPayload.Length, System.Text.Encoding.UTF8.GetString(jsonPayload.ToArray()));
                throw new ProtocolException($"OpenHAB event parsing and IEvent creation failed, {jsonPayload.Length} bytes: '{System.Text.Encoding.UTF8.GetString(jsonPayload.ToArray())}'", ex);
            }
        }

        public T Create<T>(EventType eventTypeID) where T : class, IEvent
        {
            var reqType = GetEventType(eventTypeID);
            if (!typeof(T).IsAssignableFrom(reqType))
                throw new EventException($"Event type incompatibility: cannot assign {reqType.Name} (required by EventType ID) to {typeof(T).Name} (requested CLR type)");
            if (Activator.CreateInstance(reqType) is T evt)
            {
                evt.Configure(eventTypeID);
                return evt;
            }
            throw new EventException($"Failed to create {typeof(T).FullName} object for EventType.{eventTypeID}. Likely the ID and type don't match.");
        }

        public T Create<T>(EventType eventTypeID, string itemName) where T : class, IItemEvent
        {
            return Create<T>(eventTypeID).ForItem(itemName) as T ?? throw new EventException($"Failed to create {nameof(T)} object for item {itemName}");
        }

        public IEvent Command<T>(string itemName, T status) where T : struct
        {
            return Create<ItemEventTypeValue>(EventType.ItemCommandEvent).Set<T>(status).ForItem(itemName);
        }

        public ArraySegment<byte> Serialize(IEvent eventObject)
        {
            try
            {
                return new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(eventObject, eventObject.GetType(), JsonOptions));
            }
            catch ( InvalidOperationException ex)
            {
                throw new OperationCanceledException("Likely it should be an OperationCanceledException instead.", ex);
                //throw ex;
            }
            catch ( Exception ex )
            {
                Logger.LogWarning(ex, "IEvent serialization failed. IEvent.Topic: \"{eventPath}\"", eventObject.Topic);
                throw new ProtocolException($"IEvent serialization failed. IEvent.Topic: \"{eventObject.Topic}\"", ex);
            }
        }
    }
}
