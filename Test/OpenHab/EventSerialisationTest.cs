using System.Text.Json;

namespace SRF.Network.Cli.OpenHab
{
    public class EventSerialisationTest
    {
        IEventFactory Factory;
        ILoggerFactory LoggerFactory;
        ILogger<EventSerialisationTest> Logger;

        [OneTimeSetUp]
        public void SetUp()
        {
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(c => c.AddSimpleConsole());
            Factory = new EventFactory(LoggerFactory.CreateLogger<EventFactory>());
            Logger = LoggerFactory.CreateLogger<EventSerialisationTest>();
        }

        [Test]
        public void SerializeItemCommand()
        {
            var cmd = Factory.Create(EventType.ItemCommandEvent) as ItemEventTypeValue;
            Assert.That(cmd, Is.InstanceOf<ItemEventTypeValue>(), "Wrong IEvent class created or null.");
            cmd?.ForItem("TheWeirdTestItem");
            cmd?.Set(666);
            string ser = JsonSerializer.Serialize(cmd, cmd.GetType());
            Logger.LogInformation("SerializeItemCommand, serialized: {ser}", ser);
            Assert.Multiple(() =>
            {
                Assert.That(ser.Contains("type"), "no type");
                Assert.That(ser.Contains("openhab/items/TheWeirdTestItem/command"), "wrong topic");
                Assert.That(ser.Contains("666"), "wrong / no value");
            });
        }

        [Test]
        public void SerializeWebSocketEventFilterTypes()
        {
            var cmd = Factory.CreateFilterType(new EventType[] { EventType.ItemStateEvent, EventType.ItemStateChangedEvent, EventType.ItemCommandEvent, EventType.GroupItemStateChangedEvent });
            string ser = JsonSerializer.Serialize(cmd, cmd.GetType());
            Logger.LogInformation("SerializedWebSocketEventFilterTypes, serialized: {ser}", ser);
        }
    }
}
