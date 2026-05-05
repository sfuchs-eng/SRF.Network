using System.Text.Json.Serialization;
using UnitsNet;

namespace SRF.Network.OpenHab.EventBus.Events
{
    [EventTypesMapped(new EventType[] {
            EventType.ItemAddedEvent,
            EventType.ItemRemovedEvent,
            EventType.ItemStatePredictedEvent,
            EventType.ItemUpdatedEvent,
        }, EventTypesMappedAttribute.MappingPriority.Default)
        ]
    public class ItemEvent : Event, IItemEvent
    {
        [JsonIgnore]
        public string ItemName { get => TopicTokens[2]; set => TopicTokens[2] = value; }

        public override IEvent Configure(EventType eventType)
        {
            EnsureTopicTokensInitialized();
            // no need to set 4th token as all types originate from deserialization or inheritance; so far...
            return base.Configure(eventType);
        }

        public ItemEvent ForItem(string itemName)
        {
            EnsureTopicTokensInitialized();
            ItemName = itemName;
            return this;
        }

        private readonly string TopicTokens1 = "items";

        private void EnsureTopicTokensInitialized()
        {
            if (TopicTokens.Length < 4)
                TopicTokens = new string[4] { "openhab", TopicTokens1, "", "" };
            if (string.IsNullOrWhiteSpace(TopicTokens[1]))
                TopicTokens[1] = TopicTokens1;
        }

        IItemEvent IItemEvent.ForItem(string itemName)
        {
            return ForItem(itemName);
        }
    }
}
