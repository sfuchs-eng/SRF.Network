
namespace SRF.Network.Misc;

[Obsolete("Inject TimeProvider.System instead")]
public class NowTimeProvider : ITimeProvider
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
