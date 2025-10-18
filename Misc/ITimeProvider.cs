using System;

namespace SRF.Network.Misc;

public interface ITimeProvider
{
    public DateTimeOffset Now { get; }
}
