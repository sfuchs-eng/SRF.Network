using Microsoft.Extensions.Logging;
using SRF.Knx.Config.Domain;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;

namespace SRF.Network.Knx.Dpt;

/// <summary>
/// Resolves <see cref="DptBase"/> instances for KNX group addresses by looking up the
/// ETS group address export from a <see cref="DomainConfiguration"/> that must be registered
/// in the DI service catalog by the consumer. DPT creation is delegated to <see cref="IDptFactory"/>.
/// Results are cached in memory after the first lookup.
/// </summary>
public class KnxDptResolver : IDptResolver
{
    private readonly DomainConfiguration _domainConfig;
    private readonly IDptFactory _dptFactory;
    private readonly ILogger<KnxDptResolver> _logger;

    private readonly Dictionary<ushort, DptBase> _cache = [];
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="KnxDptResolver"/>.
    /// </summary>
    public KnxDptResolver(DomainConfiguration domainConfig, IDptFactory dptFactory, ILogger<KnxDptResolver> logger)
    {
        _domainConfig = domainConfig ?? throw new ArgumentNullException(nameof(domainConfig));
        _dptFactory   = dptFactory   ?? throw new ArgumentNullException(nameof(dptFactory));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public DptBase GetDpt(GroupAddress groupAddress)
    {
        ArgumentNullException.ThrowIfNull(groupAddress);

        lock (_lock)
        {
            if (_cache.TryGetValue(groupAddress.Address, out var cached))
                return cached;

            if (!_domainConfig.GroupAddresses.TryGetValue(groupAddress.Address, out var etsConfig))
                throw new KnxException($"Group address {groupAddress} not found in ETS group address export.");

            if (!etsConfig.DPT.IsValidMainType)
                throw new KnxException($"Group address {groupAddress} ({etsConfig.Label}) has no valid DPT configured in the ETS export.");

            var dpt = _dptFactory.Get(etsConfig.DPT.Main, etsConfig.DPT.Sub);
            _cache[groupAddress.Address] = dpt;
            _logger.LogTrace("Resolved DPT {Dpt} for group address {GroupAddress} ({Label})", dpt.Id, groupAddress, etsConfig.Label);
            return dpt;
        }
    }
}
