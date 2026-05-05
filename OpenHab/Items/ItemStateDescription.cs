using System;
using System.Text.Json.Serialization;

namespace SRF.Network.OpenHab.Items
{
    public class ItemStateDescription
    {
        public string Pattern { get; set; } = String.Empty;
        public bool ReadOnly { get; set; } = false;
        //public string[] Options { get; set; } = Array.Empty<string>(); // fails to parse sometimes... but looks always []
    }

    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        UseStringEnumConverter = true,
        AllowOutOfOrderMetadataProperties = true,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    )]
    [JsonSerializable(typeof(ItemStateDescription))]
    internal partial class ItemStateDescriptionMetadataOnlyContext : JsonSerializerContext
    {}
}
