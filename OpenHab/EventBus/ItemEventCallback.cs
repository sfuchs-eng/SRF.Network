using System;
using System.Linq;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.OpenHab.EventBus
{
    public class ItemEventCallback<ExpectedEventType> where ExpectedEventType : Events.ItemEvent
    {
        public string ItemName { get; private set; }
        public EventType[] EventTypesAccepted { get; set; } = Array.Empty<EventType>();
        public EventHandler<EventReceivedEventArgs> EventHandler { get; set; }

        private readonly EventType[] ItemStateEventOnly = { EventType.ItemStateEvent };
        private readonly EventType[] ItemStateChangedEventOnly = { EventType.ItemStateChangedEvent };

        public ItemEventCallback(string itemName, EventHandler<EventReceivedEventArgs> itemEventHandler)
        {
            ItemName = itemName;
            EventHandler = itemEventHandler;
        }

        public ItemEventCallback(string itemName, EventType[] eventTypes, EventHandler<EventReceivedEventArgs> itemEventHandler)
        {
            ItemName = itemName;
            EventTypesAccepted = eventTypes;
            EventHandler = itemEventHandler;
        }

        public ItemEventCallback<ExpectedEventType> SetFilterItemStateEvent()
        {
            EventTypesAccepted = ItemStateEventOnly;
            return this;
        }

        public ItemEventCallback<ExpectedEventType> SetFilterItemStateChangedEvent()
        {
            EventTypesAccepted = ItemStateChangedEventOnly;
            return this;
        }

        public void Register(IEventBusClient eventBusClient)
        {
            eventBusClient.EventReceived += EventBusClient_EventReceived;
        }

        public void Unregister(IEventBusClient eventBusClient)
        {
            eventBusClient.EventReceived -= EventBusClient_EventReceived;
        }

        void EventBusClient_EventReceived(object? sender, EventReceivedEventArgs e)
        {
            if (!EventTypesAccepted.Any(et => et == e.Received.Type))
                return;
            if (!(e.Received is ExpectedEventType exEvt))
                return;
            if (!ItemName.Equals(exEvt.ItemName))
                return;
            EventHandler(sender, e);
        }
    }
}
