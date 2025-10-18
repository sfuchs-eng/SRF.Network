using System;
using System.Text.Json;

namespace SRF.Network.Knx.Domain.ConfigModifiers;

public interface IGroupAddressExtraConfigModifier
{
    void Modify(DomainExtraConfig domainExtraConfig);
    void Modify(JsonDocument jsonDocument);
}
