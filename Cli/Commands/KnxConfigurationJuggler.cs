using DotMake.CommandLine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.OpenHab.BaseConfig;
using SRF.Knx.Config.OpenHab;
using SRF.Knx.Core;

namespace SRF.Network.Cli.Commands;

[CliCommand(Name = "knx-configuration", Alias = "kc", Description = "Displays the KNX configuration.", Parent = typeof(Root))]
public class KnxConfigurationJuggler : HostLauncher<KnxConfigurationJuggler.Worker>
{
    [CliOption(Alias = "n", Description = "Create new domain configuration from ETS project group address export.")]
    public bool CreateDomainConfigFromEtsExport { get; set; } = false;

    [CliOption(Alias = "u", Description = "Update current domain configuration from ETS project group address export.")]
    public bool UpdateDomainConfigFromEtsExport { get; set; } = false;

    [CliOption(Alias = "lgac", Required = false, Name = "import-legacy-gac", Description = "Load domain config and override each existing GA with legacy group address config XML file's settings for that GA.")]
    public string? LegacyGACFileName { get; set; }

    [CliOption(Alias = "f", Description = "Force overwriting existing files during conversion.")]
    public bool ForceOverwrite { get; set; } = false;

    [CliOption(Alias = "o", Description = "Update OpenHAB configuration")]
    public bool UdpateOpenHabConfig { get; set; } = false;

    [CliOption(Alias = "om", Description = "Update OpenHAB meta-configuration only, do not create new OpenHAB config files.")]
    public bool UpdateOpenHabConfigMetaOnly { get; set; } = false;

    [CliOption(Alias = "rf", Description = "Remove Fresh and Changed flags from all EntryStatus properties in domain and OpenHAB configurations.")]
    public bool RemoveFreshFlag { get; set; } = false;

    [CliOption(Alias = "fc", Name = "fix-channels", Description = "Batch fix Channel entries for all Group Addresses with ChannelType 'Default' or no DPT set.")]
    public bool BatchCreateChannels { get; set; } = false;

    [CliOption(Alias = "hca", Name = "generate-homecompanion-autogen", Description = "Generate HomeCompanionKnxAutoGen.json for the HomeCompanion.Knx.CodeGen source generator.")]
    public bool GenerateHomeCompanionAutoGen { get; set; } = false;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddKnxCore();
        services.AddKnxConfig();
        services.AddKnxOpenHabConfig();
    }

    public class Worker(
        KnxConfigurationJuggler cmd,
        IOptions<KnxConfiguration> options,
        IKnxConfigFactory knxConfigFactory,
        IOpenHabKnxConfigFactory openHabKnxConfigFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<KnxConfigurationJuggler.Worker> logger,
        IServiceProvider serviceProvider
        ) : BackgroundService
    {
        private readonly KnxConfigurationJuggler cmd = cmd;
        private readonly IKnxConfigFactory knxConfigFactory = knxConfigFactory;
        private readonly KnxConfiguration config = options.Value;
        private readonly IHostApplicationLifetime applicationLifetime = applicationLifetime;
        private readonly ILogger<Worker> logger = logger;
        private readonly IServiceProvider serviceProvider = serviceProvider;
        private readonly IOpenHabKnxConfigFactory openHabKnxConfigFactory = openHabKnxConfigFactory;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (cmd.RemoveFreshFlag)
            {
                RemoveFreshFlagFromConfigurations();
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (cmd.BatchCreateChannels)
            {
                BatchCreateChannelEntries();
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(cmd.LegacyGACFileName))
            {
                ImportLegacyGAC();
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (cmd.CreateDomainConfigFromEtsExport)
            {
                var domainConfig = knxConfigFactory.CreateDomainConfigFromEtsExport();
                knxConfigFactory.SaveDomainConfig(domainConfig);
                logger.LogInformation("Created new domain configuration from ETS group address export file '{etsFile}' and saved to '{domainFile}'",
                    config.EtsGAExportFile,
                    config.KnxDomainConfigFile);
                SaveAutoGen(domainConfig);
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (cmd.GenerateHomeCompanionAutoGen)
            {
                var dc = knxConfigFactory.GetDomainConfig();
                SaveAutoGen(dc);
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (cmd.UpdateDomainConfigFromEtsExport || cmd.UdpateOpenHabConfig || cmd.UpdateOpenHabConfigMetaOnly)
            {
                var dc = knxConfigFactory.GetDomainConfig();
                knxConfigFactory.SaveDomainConfig(dc);
                logger.LogInformation("Updated domain configuration from ETS group address export file '{etsFile}' and saved to '{domainFile}'",
                    config.EtsGAExportFile,
                    config.KnxDomainConfigFile);
                SaveAutoGen(dc);
            }

            if (cmd.UdpateOpenHabConfig || cmd.UpdateOpenHabConfigMetaOnly)
            {
                var df = knxConfigFactory;
                var of = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>();

                logger.LogWarning("Using deserialization-update-serialization instead of JsonNode based delta updating of files. Implementation of delta-updating pending.");
                var dc = df.GetDomainConfig();
                KnxOpenHabConfig ohc;
                bool configSuccess = false;
                try
                {
                    logger.LogTrace("Loading existing OpenHAB KNX configuration file.");
                    ohc = of.GetKnxOpenHabConfig(dc);
                    configSuccess = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error loading existing OpenHAB KNX configuration file. Starting with fresh configuration.");
                    ohc = new();
                }

                if (configSuccess)
                {
                    logger.LogTrace("Identifying and applying configuration updates to OpenHAB KNX configuration.");
                    var updates = of.IdentifyConfigurationUpdates(dc, ohc);
                    of.ApplyConfigurationUpdates(updates, ohc);
                    of.SaveBaseConfig(ohc);
                }
                else
                {
                    logger.LogWarning("Skipping update of OpenHAB KNX configuration due to errors loading/creating the configuration.");
                }

                if ( cmd.UdpateOpenHabConfig )
                {
                    of.WriteOHConfigFiles(ohc);
                }

                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            logger.LogInformation("KNX Configuration:");
            Console.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(
                    config,
                    KnxConfigFactory.DefaultJsonOptions
                ));

            applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove Fresh and Changed flags from all EntryStatus properties in domain and OpenHAB configurations.
        /// </summary>
        private void RemoveFreshFlagFromConfigurations()
        {
            try
            {
                // Remove Fresh flag from domain configuration
                var domainConfig = knxConfigFactory.GetDomainConfig();
                int domainGACount = 0;
                foreach (var ga in domainConfig.Extra.GetAllExtraConfigs() )
                {
                    if (ga.EntryStatus.HasFlag(ExtraConfigStatus.Fresh) || ga.EntryStatus.HasFlag(ExtraConfigStatus.Changed))
                    {
                        ga.EntryStatus &= ~(ExtraConfigStatus.Fresh | ExtraConfigStatus.Changed);
                        domainGACount++;
                    }
                }
                knxConfigFactory.SaveDomainConfig(domainConfig);
                logger.LogInformation("Removed Fresh flag from {count} group addresses in domain configuration.", domainGACount);

                // Remove Fresh flag from OpenHAB configuration
                var of = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>();
                var ohConfig = of.GetKnxOpenHabConfig(domainConfig);
                int ohGACount = 0;
                foreach (var thing in ohConfig.Things)
                {
                    foreach (var ga in thing.GroupAddresses)
                    {
                        if (ga.EntryStatus.HasFlag(ExtraConfigStatus.Fresh) || ga.EntryStatus.HasFlag(ExtraConfigStatus.Changed))
                        {
                            ga.EntryStatus &= ~(ExtraConfigStatus.Fresh | ExtraConfigStatus.Changed);
                            ohGACount++;
                        }
                    }
                }
                of.SaveBaseConfig(ohConfig);
                logger.LogInformation("Removed Fresh flag from {count} group addresses in OpenHAB configuration.", ohGACount);
                logger.LogInformation("Successfully removed Fresh flag from configurations.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while removing Fresh flag from configurations.");
            }
        }

        private void SaveAutoGen(SRF.Knx.Config.Domain.DomainConfiguration dc)
        {
            var entries = knxConfigFactory.GenerateHomeCompanionAutoGen(dc);
            knxConfigFactory.SaveHomeCompanionAutoGen(entries);
            logger.LogInformation("Generated HomeCompanion auto-gen mapping with {count} entries and saved to '{file}'",
                entries.Count,
                config.HomeCompanionAutoGenFile);
        }

        /// <summary>
        /// Import legacy GAC XML configuration and convert to JSON domain and OpenHAB base configuration files.
        /// </summary>
        private void ImportLegacyGAC()
        {
            if (!File.Exists(cmd.LegacyGACFileName))
                logger.LogWarning("Legacy GAC '{}' doesn't exist.", cmd.LegacyGACFileName);
            else
            {
                openHabKnxConfigFactory.OverrideConfigsFromLegacy(cmd.LegacyGACFileName, out DomainConfiguration domainConfig, out KnxOpenHabConfig openHabConfig);
                // both are already safed.
                logger.LogInformation("Converted legacy Group Address Config '{lgac}' to JSON configuration file.", cmd.LegacyGACFileName);
            }
        }

        /// <summary>
        /// Batch create Channel entries for all Group Addresses with ChannelType 'Default' or no DPT set.
        /// Uses existing templating and lookup mechanisms based on ETS data (Label and DPT).
        /// </summary>
        private void BatchCreateChannelEntries()
        {
            try
            {
                // Load the domain and OpenHAB configurations
                var dc = knxConfigFactory.GetDomainConfig();
                var of = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>();
                
                KnxOpenHabConfig ohc;
                bool configSuccess = false;
                try
                {
                    logger.LogTrace("Loading existing OpenHAB KNX configuration file.");
                    ohc = of.GetKnxOpenHabConfig(dc);
                    configSuccess = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error loading existing OpenHAB KNX configuration file. Starting with fresh configuration.");
                    ohc = new();
                }

                if (!configSuccess)
                {
                    logger.LogError("Cannot batch create channels due to errors loading OpenHAB KNX configuration.");
                    return;
                }

                int channelsChanged = 0;
                int channelsProcessed = 0;

                // Iterate through all Things and their Group Addresses
                foreach (var thing in ohc.Things)
                {
                    foreach (var ga in thing.GroupAddresses)
                    {
                        channelsProcessed++;

                        // Check if Channel has ChannelType.Default or no DPT set
                        bool isChannelDefault = ga.Channel.Type == SRF.Knx.Config.OpenHab.Generate.ChannelType.Default;
                        bool hasDptIssue = ga.Channel.DPT == null || !ga.Channel.DPT.IsValidType;

                        if (!(isChannelDefault || hasDptIssue))
                            continue;

                        logger.LogDebug("Processing GA {ga} '{gaLabel}' with ChannelType={channelType}, HasValidDPT={hasDpt}",
                            ga.Address3L, ga.Name, ga.Channel.Type, ga.Channel.DPT?.IsValidType ?? false);

                        if ( !dc.GroupAddresses.TryGetValue(ga.Address.Address, out var etsGac))
                        {
                            logger.LogWarning("GA {ga} from OpenHAB config not found in domain configuration.", ga.Address3L);
                            continue;
                        }

                        // Get the extra configuration which may have domain-level settings
                        if (!dc.Extra.TryGetGAExtraConfig(ga.Address, out var extraConfig))
                        {
                            logger.LogWarning("Extra config for GA {ga} not found.", ga.Address3L);
                            continue;
                        }

                        // Recreate the OpenHAB GA config using the factory, which applies templating
                        var recreatedOhGa = of.CreateOpenHabGAC(etsGac.Address, dc);

                        // If the template provided a valid channel config, update the GA
                        if (recreatedOhGa.Channel.Type != SRF.Knx.Config.OpenHab.Generate.ChannelType.Default &&
                            recreatedOhGa.Channel.Type != SRF.Knx.Config.OpenHab.Generate.ChannelType.NotSupported)
                        {
                            ga.Channel.DPT = etsGac.DPT;
                            ga.Channel.Type = recreatedOhGa.Channel.Type;
                            ga.EntryStatus |= ExtraConfigStatus.Changed;

                            // Update other channel properties as needed
                            if (string.IsNullOrEmpty(ga.Channel.Name))
                            {
                                ga.Channel.Name = recreatedOhGa.Channel.Name;
                            }

                            logger.LogInformation("Updated GA {gaAddress} '{gaLabel}': ChannelType={channelType}, DPT={dpt}",
                                ga.Address3L, ga.Name, ga.Channel.Type, ga.Channel.DPT?.DotFormat ?? "undefined");
                            channelsChanged++;
                        }
                        else
                        {
                            logger.LogWarning("No valid channel template found for GA {gaAddress} '{gaLabel}'",
                                ga.Address3L, ga.Name);
                        }
                    }
                }

                // Save the updated OpenHAB configuration
                of.SaveBaseConfig(ohc);
                
                logger.LogInformation("Batch channel updates completed: {created} channels updated out of {processed} group addresses processed.",
                    channelsChanged, channelsProcessed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during batch channel creation.");
            }
        }
    }
}
