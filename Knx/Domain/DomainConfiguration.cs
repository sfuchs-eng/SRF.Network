namespace SRF.Network.Knx.Domain;

/// <summary>
/// Reflects the KNX related configuration of a domain of 16-bit group addresses
/// and IoT nodes, typically equivalent to an ETS project.
/// </summary>
public class DomainConfiguration
{
    /// <summary>
    /// ETS exported Group Address configurations by their 16-bit address.
    /// </summary>
    public Dictionary<ushort, GroupAddressConfiguration> GroupAddresses { get; init; } = [];

    public DomainExtraConfig Extra { get; set; } = new();
}
