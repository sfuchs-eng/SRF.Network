using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(EventType.WebSocketEvent)]
    public class WebSocketEvent : Event
    {
        [JsonIgnore]
        public bool IsFilter { get => TopicTokens.Length >= 4 && TopicTokens[2].Equals("filter"); }
        [JsonIgnore]
        public bool IsFilterSource { get => IsFilter && TopicTokens[3].Equals("source"); }
        [JsonIgnore]
        public bool IsFilterType { get => IsFilter && TopicTokens[3].Equals("type"); }

        [JsonIgnore]
        public bool IsHeartbeat { get => TopicTokens.Length >= 3 && TopicTokens[2].Equals("heartbeat"); }
        [JsonIgnore]
        public bool IsPing { get => IsHeartbeat && "PING".Equals(PayloadJson); }
        [JsonIgnore]
        public bool IsPong { get => IsHeartbeat && "PONG".Equals(PayloadJson); }

        [JsonIgnore]
        public bool IsResponseFailed { get => TopicTokens.Length >= 4 && TopicTokens[2].Equals("response") && TopicTokens[3].Equals("failed"); }

        public WebSocketEvent() : base()
        {
            TopicTokens = new string[4] { "openhab", "websocket", "", ""};
            Type = EventType.WebSocketEvent;
        }

        public override IEvent Configure(EventType eventType)
        {
            return base.Configure(EventType.WebSocketEvent);
        }
    }


    public static class WebSocketEventHelpers
    {
        public static readonly string TopicHeartbeat = "openhab/websocket/heartbeat";
        public static readonly string TopicFilterSource = "openhab/websocket/filter/source";
        public static readonly string TopicFilterType = "openhab/websocket/filter/type";

        public static WebSocketEvent CreateFilterSource(this IEventFactory factory, string[] sourcesRemoved)
        {
            var wse = factory.Create<WebSocketEvent>(EventType.WebSocketEvent);
            wse.Topic = TopicFilterSource;
            wse.PayloadJson = JsonSerializer.Serialize<string[]>(sourcesRemoved);
            return wse;
        }

        public static WebSocketEvent CreateFilterType(this IEventFactory factory, EventType[] eventTypesToReceive)
        {
            var wse = factory.Create<WebSocketEvent>(EventType.WebSocketEvent);
            wse.Topic = TopicFilterType;
            wse.PayloadJson = JsonSerializer.Serialize<string[]>(eventTypesToReceive.Select(t => t.GetTypeName()).ToArray());
            return wse;
        }

        public static WebSocketEvent CreatePing(this IEventFactory factory)
        {
            var wse = factory.Create<WebSocketEvent>(EventType.WebSocketEvent);
            wse.Topic = TopicHeartbeat;
            wse.PayloadJson = "PING";
            return wse;
        }
    }
}
