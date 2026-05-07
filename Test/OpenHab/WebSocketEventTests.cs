using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class WebSocketEventTests
{
    private static IEventFactory CreateFactory() => new EventFactory(NullLogger<EventFactory>.Instance);

    [Test]
    public void CreatePing_SetsHeartbeatTopicAndPingFlags()
    {
        var eventFactory = CreateFactory();

        var ping = eventFactory.CreatePing();

        Assert.Multiple(() =>
        {
            Assert.That(ping.Topic, Is.EqualTo(WebSocketEventHelpers.TopicHeartbeat));
            Assert.That(ping.PayloadJson, Is.EqualTo("PING"));
            Assert.That(ping.IsHeartbeat, Is.True);
            Assert.That(ping.IsPing, Is.True);
            Assert.That(ping.IsPong, Is.False);
        });
    }

    [Test]
    public void CreateFilterSource_SetsFilterTopicAndPayload()
    {
        var eventFactory = CreateFactory();

        var filter = eventFactory.CreateFilterSource(["self", "rules"]);

        Assert.Multiple(() =>
        {
            Assert.That(filter.Topic, Is.EqualTo(WebSocketEventHelpers.TopicFilterSource));
            Assert.That(filter.IsFilterSource, Is.True);
            Assert.That(JsonSerializer.Deserialize<string[]>(filter.PayloadJson), Is.EqualTo(new[] { "self", "rules" }));
        });
    }

    [Test]
    public void CreateFilterType_SetsFilterTopicAndPayload()
    {
        var eventFactory = CreateFactory();

        var filter = eventFactory.CreateFilterType([EventType.ItemCommandEvent, EventType.ItemStateEvent]);

        Assert.Multiple(() =>
        {
            Assert.That(filter.Topic, Is.EqualTo(WebSocketEventHelpers.TopicFilterType));
            Assert.That(filter.IsFilterType, Is.True);
            Assert.That(JsonSerializer.Deserialize<string[]>(filter.PayloadJson), Is.EqualTo(new[] { "ItemCommandEvent", "ItemStateEvent" }));
        });
    }

    [Test]
    public void ResponseFailedTopic_SetsResponseFailedFlag()
    {
        var webSocketEvent = new WebSocketEvent
        {
            Topic = "openhab/websocket/response/failed",
            PayloadJson = "failure",
        };

        Assert.That(webSocketEvent.IsResponseFailed, Is.True);
    }

    [Test]
    public void PongPayload_SetsPongFlag()
    {
        var webSocketEvent = new WebSocketEvent
        {
            Topic = WebSocketEventHelpers.TopicHeartbeat,
            PayloadJson = "PONG",
        };

        Assert.Multiple(() =>
        {
            Assert.That(webSocketEvent.IsHeartbeat, Is.True);
            Assert.That(webSocketEvent.IsPong, Is.True);
            Assert.That(webSocketEvent.IsPing, Is.False);
        });
    }
}