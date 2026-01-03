using DotMake.CommandLine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.OpenHab.BaseConfig;

namespace SRF.Network.Cli.Commands;

[CliCommand(Name = "knx-configuration", Alias = "kc", Description = "Displays the KNX configuration.", Parent = typeof(Root))]
public class KnxConfigurationJuggler : HostLauncher<KnxConfigurationJuggler.Worker>
{
    [CliOption(Alias = "n", Description = "Create new domain configuration from ETS project group address export.")]
    public bool CreateDomainConfigFromEtsExport { get; set; } = false;

    [CliOption(Alias = "d", Description = "Update current domain configuration from ETS project group address export.")]
    public bool UpdateDomainConfigFromEtsExport { get; set; } = false;

    [CliOption(Alias = "lgac", Required = false, Name = "import-legacy-gac", Description = "Load domain config and override each existing GA with legacy group address config XML file's settings for that GA.")]
    public string? LegacyGACFileName { get; set; }

    [CliOption(Alias = "f", Description = "Force overwriting existing files during conversion.")]
    public bool ForceOverwrite { get; set; } = false;

    [CliOption(Alias = "o", Description = "Update OpenHAB configuration")]
    public bool UdpateOpenHabConfig { get; set; } = false;

    [CliOption(Alias = "om", Description = "Update OpenHAB meta-configuration only, do not create new OpenHAB config files.")]
    public bool UpdateOpenHabConfigMetaOnly { get; set; } = false;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddKnxConfig();
    }

    public class Worker(
        KnxConfigurationJuggler cmd,
        IOptions<KnxConfiguration> options,
        IKnxConfigFactory knxConfigFactory,
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

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!string.IsNullOrEmpty(cmd.LegacyGACFileName))
            {
                ImportLegacyGAC();
                applicationLifetime.StopApplication();
                return Task.CompletedTask;
            }

            if (cmd.CreateDomainConfigFromEtsExport)
            {
                knxConfigFactory.CreateDomainConfigFromEtsExport();
                logger.LogInformation("Created new domain configuration from ETS group address export file '{etsFile}' and saved to '{domainFile}'",
                    config.EtsGAExportFile,
                    config.KnxDomainConfigFile);
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
        /// Import legacy GAC XML configuration and convert to JSON domain and OpenHAB base configuration files.
        /// </summary>
        private void ImportLegacyGAC()
        {
            if (!File.Exists(cmd.LegacyGACFileName))
                logger.LogWarning("Legacy GAC '{}' doesn't exist.", cmd.LegacyGACFileName);
            else
            {
                knxConfigFactory.OverrideConfigsFromLegacy(cmd.LegacyGACFileName, out DomainConfiguration domainConfig, out KnxOpenHabConfig openHabConfig);
                // both are already safed.
                logger.LogInformation("Converted legacy Group Address Config '{lgac}' to JSON configuration file.", cmd.LegacyGACFileName);
            }
        }
    }
}
