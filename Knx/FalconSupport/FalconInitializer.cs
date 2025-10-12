using Knx.Falcon.Logging;
using KNX = Knx.Falcon;

namespace SRF.Network.Knx.FalconSupport;

/// <summary>
/// Initializations of the Falcon SDK that need to be completed prior instanciation of any Falcon SDK class.
/// </summary>
public class FalconInitializer
{
    public FalconInitializer(IFalconLoggerFactory falconLoggerFactory)
    {
        KNX.Logging.Logger.Factory = falconLoggerFactory;
    }
}
