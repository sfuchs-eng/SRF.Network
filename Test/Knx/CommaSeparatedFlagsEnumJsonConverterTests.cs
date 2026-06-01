using System.Text.Json;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;

namespace SRF.Network.Test.Knx;

public class CommaSeparatedFlagsEnumJsonConverterTests
{
    [TestCase(ExtraConfigStatus.Automatic | ExtraConfigStatus.Fresh, "\"Automatic, Fresh\"")]
    [TestCase(ExtraConfigStatus.Fresh | ExtraConfigStatus.Automatic, "\"Automatic, Fresh\"")]
    public void ExtraConfigStatus_is_serialized_as_comma_separated_string_flags(ExtraConfigStatus value, string expectedJson)
    {
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo(expectedJson));
    }

    [TestCase("\"Fresh, Automatic\"")]
    [TestCase("\"Automatic, Fresh\"")]
    public void ExtraConfigStatus_is_deserialized_independently_of_token_order(string json)
    {
        var value = JsonSerializer.Deserialize<ExtraConfigStatus>(json);

        Assert.That(value, Is.EqualTo(ExtraConfigStatus.Automatic | ExtraConfigStatus.Fresh));
    }

    [TestCase(KnxObjectBusCommunication.Read | KnxObjectBusCommunication.Write | KnxObjectBusCommunication.Transmit, "\"Read, Write, Transmit\"")]
    [TestCase(KnxObjectBusCommunication.Communication, "\"Read, Write, Transmit, Update, Initialize\"")]
    public void KnxObjectBusCommunication_is_serialized_as_comma_separated_string_flags(KnxObjectBusCommunication value, string expectedJson)
    {
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo(expectedJson));
    }

    [TestCase("\"Write, Read, Transmit\"")]
    [TestCase("\"Communication\"")]
    public void KnxObjectBusCommunication_is_deserialized_independently_of_token_order(string json)
    {
        var value = JsonSerializer.Deserialize<KnxObjectBusCommunication>(json);

        Assert.That(value.HasFlag(KnxObjectBusCommunication.Read), Is.True);
        Assert.That(value.HasFlag(KnxObjectBusCommunication.Write), Is.True);
        Assert.That(value.HasFlag(KnxObjectBusCommunication.Transmit), Is.True);
    }

    [TestCase("\"Read, NotAFlag\"")]
    [TestCase("42")]
    [TestCase("\"Read,,Write\"")]
    public void Knx_flags_reject_invalid_json_tokens(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KnxObjectBusCommunication>(json));
    }
}