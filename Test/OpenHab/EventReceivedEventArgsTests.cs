using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class EventReceivedEventArgsTests
{
    [Test]
    public void IsItem_WithMatchingNameAndItemEvent_ReturnsTrue()
    {
        var itemEvent = new ItemEventTypeValue();
        itemEvent.Configure(EventType.ItemStateEvent);
        itemEvent.ForItem("HallwayMotion");
        itemEvent.Set(ItemStateSwitch.ON);
        var args = new EventReceivedEventArgs(itemEvent, DateTimeOffset.UtcNow);

        Assert.That(args.IsItem("HallwayMotion"), Is.True);
    }

    [Test]
    public void IsItem_WithWrongName_ReturnsFalse()
    {
        var itemEvent = new ItemEventTypeValue();
        itemEvent.Configure(EventType.ItemStateEvent);
        itemEvent.ForItem("HallwayMotion");
        itemEvent.Set(ItemStateSwitch.ON);
        var args = new EventReceivedEventArgs(itemEvent, DateTimeOffset.UtcNow);

        Assert.That(args.IsItem("KitchenMotion"), Is.False);
    }

    [Test]
    public void IsItem_GenericWithMatchingType_ReturnsTypedInstance()
    {
        var itemEvent = new ItemStateChangedEvent
        {
            StateChange = new ItemStateChangedEventPayload
            {
                Type = "StateChangedEvent",
                Value = "ON",
                OldType = "StateChangedEvent",
                OldValue = "OFF",
            },
        }.ForItem("HallwayMotion");
        var args = new EventReceivedEventArgs(itemEvent, DateTimeOffset.UtcNow);

        var isMatch = args.IsItem<ItemStateChangedEvent>("HallwayMotion", out var typedEvent);

        Assert.Multiple(() =>
        {
            Assert.That(isMatch, Is.True);
            Assert.That(typedEvent.ItemName, Is.EqualTo("HallwayMotion"));
            Assert.That(typedEvent.StateChange.Value, Is.EqualTo("ON"));
        });
    }

    [Test]
    public void IsItem_GenericWithNonMatchingType_ReturnsFalse()
    {
        var webSocketEvent = new WebSocketEvent
        {
            Topic = WebSocketEventHelpers.TopicHeartbeat,
            PayloadJson = "PING",
        };
        var args = new EventReceivedEventArgs(webSocketEvent, DateTimeOffset.UtcNow);

        var isMatch = args.IsItem<ItemStateChangedEvent>("HallwayMotion", out _);

        Assert.That(isMatch, Is.False);
    }
}