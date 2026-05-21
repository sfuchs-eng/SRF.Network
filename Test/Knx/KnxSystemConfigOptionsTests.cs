using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;

namespace SRF.Network.Test.Knx;

[TestFixture]
public class KnxSystemConfigOptionsTests
{
    [Test]
    public void AddKnxConfig_BindsKnxSystemSection_ToKnxSystemConfigOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Knx:System:EtsGAExportFile", "/project/GroupAddresses.xml"),
                new("Knx:System:KnxDomainConfigFile", "/project/KnxDomainConfig.json"),
                new("Knx:System:HomeCompanionCodeGenFile", "/project/KnxValues.generated.cs"),
                new("Knx:System:KnxMasterFolder", "/usr/share/ets/knx-master"),
                new("Knx:System:LinkKnxValuesToOpenHabForInitialization", "false"),
                new("Knx:System:OpenHab:BaseConfigFile", "/project/OHBase.json"),
                new("Knx:System:OpenHab:TemplatesFolder", "/project/templates"),
                new("Knx:System:OpenHab:OHConfigRoot", "/etc/openhab"),
                new("Knx:System:OpenHab:OpenHabVersion", "3"),
                new("Knx:System:OpenHab:WaitTimeBeforeWritingThingsFileSec", "5"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddKnxConfig();

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KnxSystemConfigOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(opts.EtsGAExportFile, Is.EqualTo("/project/GroupAddresses.xml"));
            Assert.That(opts.KnxDomainConfigFile, Is.EqualTo("/project/KnxDomainConfig.json"));
            Assert.That(opts.HomeCompanionCodeGenFile, Is.EqualTo("/project/KnxValues.generated.cs"));
            Assert.That(opts.KnxMasterFolder, Is.EqualTo("/usr/share/ets/knx-master"));
            Assert.That(opts.LinkKnxValuesToOpenHabForInitialization, Is.False);
            Assert.That(opts.OpenHab.BaseConfigFile, Is.EqualTo("/project/OHBase.json"));
            Assert.That(opts.OpenHab.TemplatesFolder, Is.EqualTo("/project/templates"));
            Assert.That(opts.OpenHab.OHConfigRoot, Is.EqualTo("/etc/openhab"));
            Assert.That(opts.OpenHab.OpenHabVersion, Is.EqualTo("3"));
            Assert.That(opts.OpenHab.WaitTimeBeforeWritingThingsFileSec, Is.EqualTo(5));
        });
    }

    [Test]
    public void AddKnxConfig_UsesDefaults_WhenNoConfigProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddKnxConfig();

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KnxSystemConfigOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(opts.EtsGAExportFile, Is.EqualTo("GroupAddressExport.xml"));
            Assert.That(opts.KnxDomainConfigFile, Is.EqualTo("KnxDomainConfig.json"));
            Assert.That(opts.LinkKnxValuesToOpenHabForInitialization, Is.True);
            Assert.That(opts.OpenHab.OpenHabVersion, Is.EqualTo("5"));
            Assert.That(opts.OpenHab.WaitTimeBeforeWritingThingsFileSec, Is.EqualTo(20));
        });
    }

    [Test]
    public void AddKnxConfig_AcceptsCustomSectionName()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Custom:Knx:EtsGAExportFile", "/custom/export.xml"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddKnxConfig("Custom:Knx");

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KnxSystemConfigOptions>>().Value;

        Assert.That(opts.EtsGAExportFile, Is.EqualTo("/custom/export.xml"));
    }
}
