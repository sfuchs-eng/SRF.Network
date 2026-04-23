using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core.Master;

namespace SRF.Network.Cli;

/// <summary>
/// Loads KNX master data from the folder configured in <see cref="KnxConfiguration.KnxMasterFolder"/>.
/// </summary>
internal sealed class KnxMasterDataProvider(IOptions<KnxConfiguration> options) : SRF.Knx.Core.Master.KnxMasterDataProvider
{
    private KnxMasterData? _cache;

    public override KnxMasterData GetMasterData()
    {
        _cache ??= GetMasterDataFromFile(
            Path.Combine(options.Value.KnxMasterFolder, "knx_master.xml"));
        return _cache;
    }
}
