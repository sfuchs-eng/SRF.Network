using System;

namespace SRF.Network.Misc;

[Obsolete("Use TimeProvider.System instead")]
public interface ITimeProvider
{
    public DateTimeOffset Now { get; }
}
