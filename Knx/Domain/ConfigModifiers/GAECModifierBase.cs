using System;
using System.Text.Json;

namespace SRF.Network.Knx.Domain.ConfigModifiers;

public abstract class GAECModifierBase : IGroupAddressExtraConfigModifier
{
    public abstract void Modify(DomainExtraConfig domainExtraConfig);
    public abstract void Modify(JsonDocument jsonDocument);
}
