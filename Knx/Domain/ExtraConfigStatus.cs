using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRF.Network.Knx.Domain;

[Flags]
[JsonConverter(typeof(ExtraConfigStatusJsonConverter))]
public enum ExtraConfigStatus
{
    /// <summary>
    /// Programmatically added. Ok to change automatically, reflecting changes in the ETS Group Address export.
    /// </summary>
    Automatic = 1 << 0,

    /// <summary>
    /// Manual edits, do not override with automatic changes.
    /// </summary>
    Manual = 1 << 1,

    /// <summary>
    /// There's no such Group Address in the ETS Group Address export
    /// </summary>
    Surplus= 1 << 2,

    /// <summary>
    /// Newly added automatically
    /// </summary>
    Fresh = 1 << 3,
}

public class ExtraConfigStatusJsonConverter : JsonConverter<ExtraConfigStatus>
{
    public override ExtraConfigStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        var tok = str?.Split(",").Select(s => s.Trim()) ?? [];
        return tok.Select(t => Enum.Parse<ExtraConfigStatus>(t))
            .Aggregate((a, b) => a | b);
    }

    public override void Write(Utf8JsonWriter writer, ExtraConfigStatus value, JsonSerializerOptions options)
    {
        var allFlags = Enum.GetValues<ExtraConfigStatus>();
        var setFlags = allFlags.Where(f => value.HasFlag(f));
        writer.WriteStringValue(string.Join(", ", setFlags));
    }
}