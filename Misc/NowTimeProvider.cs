
namespace SRF.Network.Misc;

public class NowTimeProvider : ITimeProvider
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
