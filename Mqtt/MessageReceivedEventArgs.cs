using System.ComponentModel;
using System.Net;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace SRF.Network.Mqtt;

public class MessageReceivedEventArgs(MqttApplicationMessageReceivedEventArgs arg) : EventArgs
{
    public MqttApplicationMessageReceivedEventArgs MqttArg { get; } = arg;

    public MqttApplicationMessage ApplicationMessage { get => MqttArg.ApplicationMessage; }
    public string Topic { get => MqttArg.ApplicationMessage.Topic; }
    public string PayloadUtf8 { get => System.Text.Encoding.UTF8.GetString(MqttArg.ApplicationMessage.PayloadSegment.Array ?? throw MissingPayload()); }

    private Func<ProtocolViolationException> MissingPayload = () => new ProtocolViolationException("No payload");

    public async Task<JsonDocument> GetJsonPayloadAsync(CancellationToken cancel, JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        using var stream = new MemoryStream(ApplicationMessage.PayloadSegment.Array ?? throw MissingPayload());
        return await JsonDocument.ParseAsync(stream, options, cancel);
    }

    public JsonDocument GetJsonPayload(JsonDocumentOptions options = default(JsonDocumentOptions))
    {
        return JsonDocument.Parse(ApplicationMessage.PayloadSegment.Array, options);
    }

    public async Task<TObject> DeserializeJsonPayloadAsync<TObject>(CancellationToken cancel, JsonSerializerOptions? options = default(JsonSerializerOptions)) where TObject : class
    {
        using var stream = new MemoryStream(ApplicationMessage.PayloadSegment.Array ?? throw MissingPayload());
        return await JsonSerializer.DeserializeAsync<TObject>(stream, options, cancel)
            ?? throw new ProtocolViolationException();
    }

    public TObject DeserializeJsonPayload<TObject>(JsonSerializerOptions? options = default(JsonSerializerOptions)) where TObject : class
    {
        //using var stream = new MemoryStream(ApplicationMessage.Payload);
        return JsonSerializer.Deserialize<TObject>(ApplicationMessage.PayloadSegment.Array, options)
            ?? throw new ProtocolViolationException();
    }
}
