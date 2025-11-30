using System.Text.Json;
using System.Xml.Serialization;
using DotMake.CommandLine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SRF.Knx.Config;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.OpenHab.MetaConfig;
using SRF.Knx.Config.OpenHab;

namespace SRF.Network.Cli.Commands;

[CliCommand(Name = "knx-configuration", Alias = "kc", Description = "Displays the KNX configuration.", Parent = typeof(Root))]
public class KnxConfigurationJuggler : HostLauncher<KnxConfigurationJuggler.Worker>
{
    [CliOption(Alias = "x", Description = "Convert legacy XML configuration to JSON files.")]
    public bool ConvertXmlToJson { get; set; } = false;

    [CliOption(Alias = "lgac", Required = false, Name = "legacy-gac-file", Description = "Full file name of legacy group address configuration file. Use in combination with -x")]
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
            if (cmd.ConvertXmlToJson)
            {
                ConvertConfigurationXmlToJson();
            }
            else if ( cmd.UdpateOpenHabConfig || cmd.UpdateOpenHabConfigMetaOnly )
            {
                var df = serviceProvider.GetRequiredService<IKnxConfigFactory>();
                var of = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>();

                logger.LogWarning("Using deserialization-update-serialization instead of JsonNode based delta updating of files. Implementation of delta-updating pending.");
                var dc = df.GetDomainConfig();
                KnxOpenHabConfig ohc;
                try
                {
                    ohc = of.GetKnxOpenHabConfig(dc);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error loading existing OpenHAB KNX configuration file. Starting with fresh configuration.");
                    ohc = new();
                }
                var updates = of.IdentifyConfigurationUpdates(dc, ohc);
                of.ApplyConfigurationUpdates(updates, ohc);
                of.SaveMetaConfig(ohc);
                //cmd.JsonOutput(updates);

                //if ( !cmd.UpdateOpenHabConfigMetaOnly )
                //    df.GenerateUpdatedOpenHabConfig();
            }
            else
            {
                logger.LogInformation("KNX Configuration:");
                Console.WriteLine(
                    System.Text.Json.JsonSerializer.Serialize(
                        config,
                        KnxConfigFactory.DefaultJsonOptions
                    ));
            }
            applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }

        [Obsolete]
        private bool PrepFileConversion(string inFileName, out string xmlFile, out string jsonFile)
        {
            logger.LogTrace("Trying to convert item template file '{inFileName}'", inFileName);

            if (Path.GetExtension(inFileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogTrace("Input file '{inFileName}' has .xml extension.", inFileName);
                xmlFile = inFileName;
                jsonFile = Path.ChangeExtension(xmlFile, ".json");
            }
            else
            {
                logger.LogTrace("Input file '{inFileName}' does not have .xml extension, assuming source-target swap", inFileName);
                xmlFile = Path.ChangeExtension(inFileName, ".xml");
                jsonFile = inFileName;
            }

            if (!File.Exists(xmlFile))
            {
                logger.LogError("Input XML file '{xmlFile}' does not exist.", xmlFile);
                return false;
            }
            if (File.Exists(jsonFile) && !cmd.ForceOverwrite)
            {
                logger.LogWarning("Output JSON file '{jsonFile}' already exists. Skipping conversion. Use -f to force overwrite.", jsonFile);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Convert legacy XML configuration files to JSON format.
        /// </summary>
        private void ConvertConfigurationXmlToJson()
        {
            if (!string.IsNullOrWhiteSpace(cmd.LegacyGACFileName))
            {
                if (!File.Exists(cmd.LegacyGACFileName))
                    logger.LogWarning("Legacy GAC '{}' doesn't exist.", cmd.LegacyGACFileName);
                else
                {
                    knxConfigFactory.CreateConfigsFromLegacy(cmd.LegacyGACFileName, out DomainConfiguration domainConfig, out KnxOpenHabConfig openHabConfig);
                    knxConfigFactory.SaveDomainConfig(domainConfig);
                    var ohf = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>();
                    ohf.SaveMetaConfig(openHabConfig);
                }
            }
            else
            {
                logger.LogInformation("Skipping legacy Group Address Config conversion. Specify file using -lgac option to have it converted.");
            }
        }

        [Obsolete]
        public void ConvertXmlToJson<TRootClass>(string xmlFileName, string jsonFileName) where TRootClass : class
        {
            var xmlFile = new FileInfo(xmlFileName);
            if (!xmlFile.Exists)
            {
                logger.LogWarning("The input file '{fileName}' does not exist.", xmlFile.FullName);
                return;
            }

            try
            {
                XmlSerializer serializer = new(typeof(TRootClass));
                using var stream = xmlFile.OpenRead();
                var templates = (TRootClass?)serializer.Deserialize(stream)
                    ?? throw new InvalidDataException($"Could not deserialize {nameof(TRootClass)} from '{xmlFile.FullName}'");

                var jsonFile = new FileInfo(jsonFileName);
                var jsonOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
                };
                using var outstream = jsonFile.OpenWrite();
                JsonSerializer.Serialize(outstream, templates, jsonOptions);
                outstream.Flush();
                outstream.Close();
                logger.LogInformation("Converted XML file '{xmlFile}' to JSON file '{jsonFile}'", xmlFile.FullName, jsonFile.FullName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error converting XML file '{xmlFile}' to JSON", xmlFile.FullName);
            }
        }
    }
}
