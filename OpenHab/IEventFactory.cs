using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SRF.Network.OpenHab.EventBus;

namespace SRF.Network.OpenHab
{
    public interface IEventFactory
    {
        IEvent Command<T>(string itemName, T status) where T : struct;
        IEvent Create(EventType eventType);
        T Create<T>(EventType eventType) where T : class, IEvent;
        T Create<T>(EventType eventType, string itemName) where T : class, IItemEvent;
        IEvent Create(MemoryStream jsonPayload);
        IEvent Create(JsonDocument message);
        ArraySegment<byte> Serialize(IEvent eventObject);
        JsonSerializerOptions JsonOptions { get; set; }
        JsonDocumentOptions JsonDocOptions { get; set; }
    }
}
