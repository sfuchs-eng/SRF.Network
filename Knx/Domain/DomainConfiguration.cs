namespace SRF.Network.Knx.Domain;

/// <summary>
/// Reflects the KNX related configuration of a domain of 16-bit group addresses
/// and IoT nodes, typically equivalent to an ETS project.
/// </summary>
public class DomainConfiguration
{
    public Dictionary<ushort, GroupAddressConfiguration> GroupAddresses { get; init; } = [];
}
