using System.Buffers;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using MQTTnet;

namespace SRF.Network.Mqtt;

/// <summary>
/// Presently lacks proper Async methods - they're faked in context of MQTTnet 4.x to 5.x migration (no time for more).
/// </summary>
public class MessageReceivedEventArgs(MqttApplicationMessageReceivedEventArgs arg) : EventArgs
{
    public MqttApplicationMessageReceivedEventArgs MqttArg { get; } = arg;

    public MqttApplicationMessage ApplicationMessage { get => MqttArg.ApplicationMessage; }
    public string Topic { get => MqttArg.ApplicationMessage.Topic; }
    public string PayloadUtf8 { get => MqttArg.ApplicationMessage.ConvertPayloadToString(); }

    private Func<ProtocolViolationException> MissingPayload = () => new ProtocolViolationException("No payload");

    public async Task<JsonDocument> GetJsonPayloadAsync(CancellationToken cancel, JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        await Task.CompletedTask;
        return JsonDocument.Parse(ApplicationMessage.Payload, options);
    }

    public JsonDocument GetJsonPayload(JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        return JsonDocument.Parse(ApplicationMessage.Payload, options);
    }

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
        //using var stream = new MemoryStream(ApplicationMessage.Payload);
        var ur = new Utf8JsonReader(ApplicationMessage.Payload);
        var res = JsonSerializer.Deserialize<TObject>(ref ur, options);
        return res
            ?? throw new ProtocolViolationException();
    }
}
