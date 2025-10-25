using Knx.Falcon.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KNX = Knx.Falcon;
using SRF.Knx.Config;

namespace SRF.Network.Knx.FalconSupport;

/// <summary>
/// Initializations of the Falcon SDK that need to be completed prior instanciation of any Falcon SDK class.
/// </summary>
public class FalconInitializer
{
    public bool IsInitialized { get; private set; } = false;
    public bool IsSuccessfullyInitialized { get; private set; } = false;

    public FalconInitializer(
        IOptions<KnxConfiguration> options,
        IFalconLoggerFactory falconLoggerFactory,
        ILogger<FalconInitializer> logger
        )
    {
        KNX.Logging.Logger.Factory = falconLoggerFactory;

        var config = options.Value;

        if (IsInitialized)
            throw new ApplicationException($"{nameof(FalconInitializer)} must be used as singleton only.");
        bool success = true;

        // KNX master data sanity check
        var needKnxMasterFile = Path.Combine(config.KnxMasterFolder, "knx_master.xml");
        if (!File.Exists(needKnxMasterFile))
        {
            logger.LogWarning("There's no '{knxMasterFileName}' file. Falcon SDK functionality might be limited.", needKnxMasterFile);
            success = false;
        }

        // Group address configuration ETS export available?
        if (!File.Exists(config.EtsGAExportFile))
        {
            logger.LogWarning("There's no '{groupAddressExport}' file. KNX message handling functionality gets limited.", config.EtsGAExportFile);
            success = false;
        }

        IsInitialized = true;
        IsSuccessfullyInitialized = success;
    }
}
