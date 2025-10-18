using System;
using System.Text.Json.Serialization;

namespace SRF.Network.Knx.Domain;

public class GroupAddressExtraConfig()
{
    public ExtraConfigStatus EntryStatus { get; set; } = ExtraConfigStatus.Automatic | ExtraConfigStatus.Fresh;

    /// <summary>
    /// The Name is generated from the Label via a <see cref="ILabelToNameConverter"/>
    /// configured with the <see cref="DomainConfigurationFactory"/>.
    /// </summary>
    /// <value></value>
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GroupAddressExtraConfig? AutoLatest { get; set; }
}
