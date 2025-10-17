using System;
using SRF.Network.Knx.Domain;

namespace SRF.Network.Knx;

public interface IDomainConfigurationFactory
{
    public DomainConfiguration Load();
}
