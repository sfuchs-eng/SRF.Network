using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class EventSerializationTests
{
    private static IEventFactory CreateFactory() => new EventFactory(NullLogger<EventFactory>.Instance);

    [Test]
    public void SerializeItemCommand_IncludesTypeTopicAndValue()
    {
        var factory = CreateFactory();
        var command = factory.Create<ItemEventTypeValue>(EventType.ItemCommandEvent)
            .Set(666);
        command.ForItem("TheWeirdTestItem");

        var serialized = Encoding.UTF8.GetString(factory.Serialize(command));

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Contain("\"type\":\"ItemCommandEvent\""));
            Assert.That(serialized, Does.Contain("openhab/items/TheWeirdTestItem/command"));
            Assert.That(serialized, Does.Contain("666"));
        });
    }

    [Test]
    public void SerializeWebSocketEventFilterTypes_IncludesTopicAndEventTypeNames()
    {
        var factory = CreateFactory();
        var webSocketEvent = factory.CreateFilterType([
            EventType.ItemStateEvent,
            EventType.ItemStateChangedEvent,
            EventType.ItemCommandEvent,
            EventType.GroupItemStateChangedEvent,
        ]);

        var serialized = JsonSerializer.Serialize(webSocketEvent, webSocketEvent.GetType());

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Does.Contain(WebSocketEventHelpers.TopicFilterType));
            Assert.That(serialized, Does.Contain(nameof(EventType.ItemStateEvent)));
            Assert.That(serialized, Does.Contain(nameof(EventType.ItemStateChangedEvent)));
            Assert.That(serialized, Does.Contain(nameof(EventType.ItemCommandEvent)));
            Assert.That(serialized, Does.Contain(nameof(EventType.GroupItemStateChangedEvent)));
        });
    }
}