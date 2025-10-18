using System.Text.Json;

namespace SRF.Network.Knx.Domain.ConfigModifiers;

public class GAECAddOrModify(GroupAddressConfiguration gac, GroupAddressExtraConfig newGaec) : GAECModifierBase
{
    public GroupAddressConfiguration GAC { get; } = gac;
    public GroupAddressExtraConfig NewGAEC { get; } = newGaec;
    public ushort GroupAddressU { get => GAC.Address.ToKnxGroupAddress(); }

    public override void Modify(DomainExtraConfig domainExtraConfig)
    {
        ushort addr = GroupAddressU;

        // freshly created
        if (!domainExtraConfig.GroupAddresses.TryGetValue(addr, out var existingNode))
        {
            domainExtraConfig.GroupAddresses[addr] = NewGAEC;
            NewGAEC.EntryStatus |= ExtraConfigStatus.Fresh | ExtraConfigStatus.Automatic;
            return;
        }

        // manual status prevents modification
        if (existingNode.EntryStatus.HasFlag(ExtraConfigStatus.Manual))
        {
            // Do not override manually created entries, but keep AutoLatest up to date
            existingNode.AutoLatest = NewGAEC;
            NewGAEC.EntryStatus |= ExtraConfigStatus.Fresh | ExtraConfigStatus.Automatic;
            return;
        }

        NewGAEC.AutoLatest = null;
        NewGAEC.EntryStatus ^= ExtraConfigStatus.Fresh;
        domainExtraConfig.GroupAddresses[addr] = NewGAEC;
    }

    public override void Modify(JsonDocument jsonDocument)
    {
        throw new NotImplementedException();
    }
}
