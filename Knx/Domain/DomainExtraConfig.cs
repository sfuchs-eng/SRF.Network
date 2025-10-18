using System.Text.Json.Serialization;

namespace SRF.Network.Knx.Domain;

/// <summary>
/// Additional configuration for KNX domain elements, e.g. group addresses and their mapping to .NET CLR types, loaded from
/// an extra configuration file specified in <see cref="KnxConfiguration.KnxDomainConfigFile"/>.
/// </summary>
public class DomainExtraConfig
{
    [JsonIgnore]
    public Dictionary<ushort, GroupAddressExtraConfig> GroupAddresses { get; set; } = [];

    [JsonPropertyName("GroupAddresses")]
    public Dictionary<string, GroupAddressExtraConfig> GroupAddresses3LIndexed
    {
        get => GroupAddresses.ToDictionary(
            kvp => kvp.Key.To3LGroupAddress(),
            kvp => kvp.Value);
        set => GroupAddresses = value.ToDictionary(
            kvp => kvp.Key.ToKnxGroupAddress(),
            kvp => kvp.Value);
    }
}
