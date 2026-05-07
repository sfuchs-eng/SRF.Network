using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class EventFactoryTests
{
    private static EventFactory CreateFactory() => new(NullLogger<EventFactory>.Instance);

    [Test]
    public void Create_GenericBaseType_ReturnsMappedDerivedType()
    {
        var factory = CreateFactory();

        var itemEvent = factory.Create<ItemEvent>(EventType.ItemCommandEvent);

        Assert.Multiple(() =>
        {
            Assert.That(itemEvent, Is.TypeOf<ItemEventTypeValue>());
            Assert.That(itemEvent.Type, Is.EqualTo(EventType.ItemCommandEvent));
        });
    }

    [Test]
    public void Create_GenericConcreteType_ReturnsConcreteEvent()
    {
        var factory = CreateFactory();

        var webSocketEvent = factory.Create<WebSocketEvent>(EventType.WebSocketEvent);

        Assert.That(webSocketEvent, Is.TypeOf<WebSocketEvent>());
    }

    [Test]
    public void Create_FromMemoryStream_RoundTripsItemCommandEvent()
    {
        var factory = CreateFactory();
        var original = factory.Create<ItemEventTypeValue>(EventType.ItemCommandEvent)
            .Set(ItemStateSwitch.ON);
        original.ForItem("LivingRoomLight");
        var payload = factory.Serialize(original);
        using var stream = new MemoryStream(payload.ToArray());

        var roundTripped = factory.Create(stream);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped, Is.TypeOf<ItemEventTypeValue>());
            Assert.That(roundTripped.Type, Is.EqualTo(EventType.ItemCommandEvent));
            Assert.That(roundTripped.Topic, Is.EqualTo("openhab/items/LivingRoomLight/command"));
            Assert.That(((ItemEventTypeValue)roundTripped).OnOffIsOn(), Is.True);
        });
    }

    [Test]
    public void Create_UnrecognizedEventType_ThrowsProtocolException()
    {
        var factory = CreateFactory();
        const string json = """
            {
              "type": "DefinitelyUnknownEvent",
              "topic": "openhab/items/TestItem/unknown",
              "payload": "{}"
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.That(() => factory.Create(stream), Throws.TypeOf<ProtocolException>());
    }

    [Test]
    public void Create_GenericWithIncompatibleType_ThrowsEventException()
    {
        var factory = CreateFactory();

        Assert.That(
            () => factory.Create<ItemStateChangedEvent>(EventType.ItemCommandEvent),
            Throws.TypeOf<EventException>());
    }
}