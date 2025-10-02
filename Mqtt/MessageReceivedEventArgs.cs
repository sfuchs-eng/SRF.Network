using System.Net;
using System.Text.Json;
using MQTTnet;

namespace SRF.Network.Mqtt;

/// <summary>
/// MQTT message received event args with convenience methods to access json payload.
/// The Async methods run presently synchronous. There was no time for more while migrating from MQTTnet 4.x to 5.x. 
/// However, as the object is already received in memory at the time the event is raised, the async methods are meaningless. Hence marked them obsolete for now.
/// </summary>
public class MessageReceivedEventArgs(MqttApplicationMessageReceivedEventArgs arg) : EventArgs
{
    public MqttApplicationMessageReceivedEventArgs MqttArg { get; } = arg;

    public MqttApplicationMessage ApplicationMessage { get => MqttArg.ApplicationMessage; }
    public string Topic { get => MqttArg.ApplicationMessage.Topic; }
    public string PayloadUtf8 { get => MqttArg.ApplicationMessage.ConvertPayloadToString(); }

    [Obsolete($"Use {nameof(GetJsonPayload)} instead")]
    public async Task<JsonDocument> GetJsonPayloadAsync(CancellationToken cancel, JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        await Task.CompletedTask;
        return JsonDocument.Parse(ApplicationMessage.Payload, options);
    }

    public JsonDocument GetJsonPayload(JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        return JsonDocument.Parse(ApplicationMessage.Payload, options);
    }

    [Obsolete($"Use {nameof(DeserializeJsonPayload)} instead")]
    public async Task<TObject> DeserializeJsonPayloadAsync<TObject>(CancellationToken cancel, JsonSerializerOptions? options = default(JsonSerializerOptions)) where TObject : class
    {
        var ur = new Utf8JsonReader(ApplicationMessage.Payload);
        var res = JsonSerializer.Deserialize<TObject>(ref ur, options);
        await Task.CompletedTask;
        return res
            ?? throw new ProtocolViolationException();
    }

    public TObject DeserializeJsonPayload<TObject>(JsonSerializerOptions? options = default(JsonSerializerOptions)) where TObject : class
    {
        var ur = new Utf8JsonReader(ApplicationMessage.Payload);
        var res = JsonSerializer.Deserialize<TObject>(ref ur, options);
        return res
            ?? throw new ProtocolViolationException();
    }
}
