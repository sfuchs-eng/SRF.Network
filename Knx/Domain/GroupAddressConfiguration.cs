using System.Xml.Serialization;

namespace SRF.Network.Knx.Domain;

/// <summary>
/// A GroupAddress entry from an ETS Group Address export file, v5.<br/>
/// E.g. &lt;GroupAddress Name="E Surveillance: detektion PIR Stube-Essen" Address="2/1/5" Unfiltered="true" DPTs="DPST-1-1" /&gt;
/// </summary>
[XmlRoot("GroupAddress", Namespace = "http://knx.org/xml/ga-export/01")]
public class GroupAddressConfiguration
{
    [XmlAttribute("Name")]
    public string Label { get; set; } = string.Empty;

    [XmlAttribute]
    public string Address { get; set; } = string.Empty;

    [XmlAttribute]
    public bool Unfiltered { get; set; } = false;

    [XmlAttribute]
    public string DPTs { get; set; } = string.Empty;

    [XmlAttribute]
    public string? Description { get; set; }

    [XmlIgnore]
    public GroupAddressExtraConfig Extra { get; set; } = new();
}
