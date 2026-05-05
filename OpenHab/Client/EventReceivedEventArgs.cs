using System;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.OpenHab.Client
{
    public class EventReceivedEventArgs
    {
        public IEvent Received { get; private set; }
        public DateTimeOffset When { get; private set; }

        public EventReceivedEventArgs(IEvent receivedEvent, DateTimeOffset when)
        {
            Received = receivedEvent;
            When = when;
        }

        public bool IsItem(string itemName)
        {
            if (!(Received.Type == EventType.ItemAddedEvent
                || Received.Type == EventType.ItemStateEvent
                || Received.Type == EventType.ItemCommandEvent
                || Received.Type == EventType.ItemRemovedEvent
                || Received.Type == EventType.ItemUpdatedEvent
                || Received.Type == EventType.ItemStateChangedEvent
                || Received.Type == EventType.ItemStatePredictedEvent
                || Received.Type == EventType.GroupItemStateChangedEvent
                ))
                return false;
            if (Received is ItemEvent itemEvent)
                return itemName.Equals(itemEvent.ItemName);
            return false;
        }

        public bool IsItem<TEventType>(string itemName, out TEventType itemEvent) where TEventType : class, new()
        {
            if (IsItem(itemName) && (Received is TEventType desiredEventType))
            {
                itemEvent = desiredEventType;
                return true;
            }
            itemEvent = new TEventType();
            return false;
        }
    }
}
