using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.Knx.Domain.ConfigModifiers;
using SRF.Network.Misc;

namespace SRF.Network.Knx.Domain;

public class DomainConfigurationFactory(
    IOptionsSnapshot<KnxConfiguration> knxOptions,
    ITimeProvider timeProvider,
    ILogger<DomainConfigurationFactory> logger
) : IDomainConfigurationFactory
{
    public ILabelToNameConverter LabelToNameConverter { get; set; } = new DefaultLabelToNameConverter();

    public JsonSerializerOptions JsonOptionsExtraConfig { get; set; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private DateTimeOffset cacheTimeStamp;
    private DomainConfiguration? _cache;
    private DomainConfiguration? Cached {
        get => _cache;
        set
        {
            cacheTimeStamp = timeProvider.Now;
            _cache = value;
        }
    }
    private readonly ITimeProvider timeProvider = timeProvider;

    public DomainConfiguration Get()
    {
        return Cached ?? Load();
    }

    public DomainConfiguration Load()
    {
        // load existing extra config
        var extraConfig = LoadDomainExtraConfig();

        // import ETS group address file & generate missing / auto extra configs
        try
        {
            var res = new DomainConfiguration()
            {
                GroupAddresses = LoadGroupAddressConfigurations(extraConfig),
                Extra = extraConfig
            };
            logger.LogInformation("Loaded {gacCount} Group Address configurations from '{dcFile}'",
                res.GroupAddresses.Count, knxOptions.Value.EtsGAExportFile);

            // auto-create / update extra configs
            var modifiers = CreateNewExtraConfigs(res.GroupAddresses, extraConfig);

            // apply modifications to extra config
            ApplyExtraConfigModifiers(modifiers, res.GroupAddresses, extraConfig);
            
            Cached = res;
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "KNX domain configuration loading failed. Using blank config.");
            return new();
        }
    }

    protected DomainExtraConfig LoadDomainExtraConfig()
    {
        try
        {
            using var jsonFile = new FileStream(knxOptions.Value.KnxDomainConfigFile, FileMode.Open, FileAccess.Read);
            var res = System.Text.Json.JsonSerializer.Deserialize<DomainExtraConfig>(jsonFile)
                ?? new DomainExtraConfig();
            logger.LogInformation("Loaded domain extra configuration from '{dcFile}'",
                knxOptions.Value.KnxDomainConfigFile);
            return res;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Loading domain extra configuration from '{dcFile}' failed. Using blank config.",
                knxOptions.Value.KnxDomainConfigFile);
            return new DomainExtraConfig();
        }
    }

    protected Dictionary<ushort, GroupAddressConfiguration> LoadGroupAddressConfigurations(DomainExtraConfig extraConfig)
    {
        var xdoc = XDocument.Load(knxOptions.Value.EtsGAExportFile);
        var gaElems = xdoc.Descendants().Where(e => e.Name.LocalName.Equals("GroupAddress"));
        logger.LogTrace("Got {no} GA Elements", gaElems.Count());
        var ser = new XmlSerializer(typeof(GroupAddressConfiguration));
        Dictionary<ushort, GroupAddressConfiguration> gacs = [];
        foreach (var rdr in gaElems.Select(e => e.CreateReader()))
        {
            try
            {
                var gac = ser.Deserialize(rdr) as GroupAddressConfiguration ?? throw new KnxException("Failed to deserialize GroupAddress element.");
                gacs.Add(gac.Address.ToKnxGroupAddress(), gac);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deserialized GroupAddress node: '{nodeContents}', does the namespace match?",
                    rdr.Name);
            }
        }
        return gacs;
    }

    private void ApplyExtraConfigModifiers(
        IEnumerable<IGroupAddressExtraConfigModifier> modifiers,
        Dictionary<ushort, GroupAddressConfiguration> groupAddresses,
        DomainExtraConfig extraConfig)
    {
        // modify in memory extra config
        foreach (var modifier in modifiers)
        {
            modifier.Modify(extraConfig);
        }
        
        // modify persisted extra config json file
        try
        {
            using var jsonFile = new FileStream(knxOptions.Value.KnxDomainConfigFile, FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(jsonFile, extraConfig, JsonOptionsExtraConfig);
            logger.LogInformation("Modified domain extra configuration in '{dcFile}'",
                knxOptions.Value.KnxDomainConfigFile);
            /*
            using var jsonFile = new FileStream(knxOptions.Value.KnxDomainConfigFile, FileMode.Open, FileAccess.ReadWrite);
            using var jsonDoc = JsonDocument.Parse(jsonFile);
            foreach (var modifier in modifiers)
            {
                modifier.Modify(jsonDoc); ... is not implemented yet...
            }
            */
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Modifying domain extra configuration in '{dcFile}' failed.",
                knxOptions.Value.KnxDomainConfigFile);
        }
    }

    private List<IGroupAddressExtraConfigModifier> CreateNewExtraConfigs(
        Dictionary<ushort, GroupAddressConfiguration> groupAddresses,
        DomainExtraConfig extraConfig)
    {
        List<IGroupAddressExtraConfigModifier> modifiers = [.. groupAddresses
            .Select(gac =>
                new GAECAddOrModify(
                    gac.Value,
                    CreateNewExtraConfigFromGAC(gac.Value)
                        ?? throw new InvalidOperationException("Failed to create new GAEC from GAC of address " + gac.Key.To3LGroupAddress())
                    ) as IGroupAddressExtraConfigModifier
                )];
        return modifiers;
    }

    private GroupAddressExtraConfig CreateNewExtraConfigFromGAC(GroupAddressConfiguration gac)
    {
        var gaec = new GroupAddressExtraConfig()
        {
            Name = LabelToNameConverter.GetName(gac.Label),
        };
        return gaec;
    }

}
