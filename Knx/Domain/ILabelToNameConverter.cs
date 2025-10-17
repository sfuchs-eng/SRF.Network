using System;

namespace SRF.Network.Knx.Domain;

public interface ILabelToNameConverter
{
    public string GetName(string label);
}
