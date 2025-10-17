using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SRF.Network.Knx.Domain;

public class DomainConfigurationFactory(
    IOptionsSnapshot<KnxConfiguration> knxOptions,
    ILogger<DomainConfigurationFactory> logger
) : IDomainConfigurationFactory
{
    public ILabelToNameConverter LabelToNameConverter { get; set; } = new DefaultLabelToNameConverter();
    
    public DomainConfiguration Load()
    {
        try
        {
            var res = new DomainConfiguration()
            {
                GroupAddresses = LoadGroupAddressConfigurations(),
            };
            logger.LogInformation("Loaded {gacCount} Group Address configurations from '{dcFile}'",
                res.GroupAddresses.Count, knxOptions.Value.EtsGAExportFile);
            return res;
        }
        catch ( Exception ex )
        {
            logger.LogError(ex, "KNX domain configuration loading failed. Using blank config.");
            return new();
        }
    }
    
    protected Dictionary<ushort,GroupAddressConfiguration> LoadGroupAddressConfigurations()
    {
        var xdoc = XDocument.Load(knxOptions.Value.EtsGAExportFile);
        var gaElems = xdoc.Descendants().Where(e => e.Name.LocalName.Equals("GroupAddress"));
        logger.LogTrace("Got {no} GA Elements", gaElems.Count());
        var ser = new XmlSerializer(typeof(GroupAddressConfiguration));
        Dictionary<ushort,GroupAddressConfiguration> gacs = [];
        foreach (var rdr in gaElems.Select(e => e.CreateReader()))
        {
            try
            {
                var gac = ser.Deserialize(rdr) as GroupAddressConfiguration ?? throw new KnxException("Failed to deserialize GroupAddress element.");
                gac.Name = LabelToNameConverter.GetName(gac.Label);
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
}
