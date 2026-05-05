using System;
using System.Text.Json.Serialization;

namespace SRF.Network.OpenHab.Items
{
  public class Item
  {
    /*
    {
"link": "http://asgard.fu:8080/rest/items/NWO442b_Lamellen_Elternbad_rechts_Winkel",
"state": "100",
"editable": false,
"type": "Dimmer",
"name": "NWO442b_Lamellen_Elternbad_rechts_Winkel",
"label": "NW-O4.42b Lamellen Elternbad rechts: Winkel",
"category": "rollershutter",
"tags": [],
"groupNames": [
  "gStorenOGWinkel",
  "gStorenElternWinkel"
]
},
{
"link": "http://asgard.fu:8080/rest/items/WP_P129_Drehzahl",
"state": "0",
"stateDescription": {
  "pattern": "%.0f rpm",
  "readOnly": false,
  "options": []
},
"editable": false,
"type": "Number",
"name": "WP_P129_Drehzahl",
"label": "Drehzahl",
"tags": [],
"groupNames": []
},*/
    public Uri? Link { get; set; }
    public string State { get; set; } = String.Empty;
    public ItemStateDescription? StateDescription { get; set; }

    [JsonPropertyName("editable")]
    public bool IsEditable { get; set; } = false;

    public string Type { get; set; } = String.Empty;
    public string Name { get; set; } = String.Empty;
    public string Label { get; set; } = String.Empty;
    public string Category { get; set; } = String.Empty;
    public string[] Tags { get; set; } = [];
    public string[] GroupNames { get; set; } = [];
  }

  [JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true,
    AllowOutOfOrderMetadataProperties = true,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
  )]
  [JsonSerializable(typeof(Item))]
  internal partial class ItemMetadataOnlyContext : JsonSerializerContext
  {}
}
