using System;
using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Config.OpenHab;
using SRF.Knx.Core;
using SRF.Network.OpenHab;

namespace SRF.Network.Cli.Commands;

[CliCommand(Description = "OpenHAB related functions.", Parent = typeof(Root))]
public class OpenHab : HostLauncher<OpenHab.Worker>
{
    [CliOption(Alias = "i", Description = "List items")]
    public bool ListItems { get; set; } = false;

    [CliOption(Alias = "l", Description = "Connect to OpenHAB and log all events to the console.")]
    public bool LogEvents { get; set; } = false;

    [CliOption(Alias = "hc", Description = "Update HomeCompanion configuration from OpenHAB items.")]
    public bool UpdateHomeCompanionConfiguration { get; set; } = false;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddOpenHabConnector();
        services.AddKnxCore();
        services.AddKnxConfig();
        services.AddKnxOpenHabConfig();
    }

    public class Worker(
            OpenHab cmd,
            IRestApiClient openhabRestApiClient,
            IHostApplicationLifetime applicationLifetime,
            ILogger<Worker> logger,
            IServiceProvider serviceProvider
        ) : BackgroundService
    {
        private readonly OpenHab cmd = cmd;
        private readonly IRestApiClient openhabRestApiClient = openhabRestApiClient;
        private readonly IHostApplicationLifetime applicationLifetime = applicationLifetime;
        private readonly ILogger<Worker> logger = logger;
        private readonly IServiceProvider serviceProvider = serviceProvider;

        private void LogErrorAndTerminate(string message)
        {
            logger.LogError(message);
            applicationLifetime.StopApplication();
        }

        protected async Task GenerateOpenHabValuesCodeAsync(CancellationToken stoppingToken)
        {
            // get KNX configuration from the service provider and config files, not from OpenHAB (would be better, might be something for the future)
            var knxConfig = serviceProvider.GetRequiredService<IKnxConfigFactory>().GetDomainConfig();
            var openHabKnxConfig = serviceProvider.GetRequiredService<IOpenHabKnxConfigFactory>()
                .Get(knxConfig);
            var openHabKnxItemsNames = openHabKnxConfig.Things
                .SelectMany(t => t.GroupAddresses)
                .Select(c => c.Item?.Name ?? c.Name)
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // get all OpenHAB items from the OpenHAB REST API
            var allItems = await openhabRestApiClient.GetItemsAsync(stoppingToken);

            // items from OpenHAB that are not mapped to KNX group addresses
            var nonKnxItems = allItems
                .Where(i => !openHabKnxItemsNames.Contains(i.Name))
                .Select(i => new OpenHabItemInfo { Name = i.Name, Type = i.Type, State = i.State })
                .ToList();

            // get config
            var config = serviceProvider.GetRequiredService<IOptions<KnxSystemConfigOptions>>().Value;
            if (config is null)
            {
                LogErrorAndTerminate("KNX system configuration is not available. Please check your configuration.");
                return;
            }

            var filePath = config.HomeCompanion.OpenHabValuesCodeGenFilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                LogErrorAndTerminate("OpenHAB values code generation file path is not specified in the KNX system configuration. Please check your configuration.");
                return;
            }

            var nameSpace = config.HomeCompanion.GeneratedValuesClassesNamespace;
            if (string.IsNullOrWhiteSpace(nameSpace))
            {
                LogErrorAndTerminate("OpenHAB values code generation namespace is not specified in the KNX system configuration. Please check your configuration.");
                return;
            }

            var className = config.HomeCompanion.OpenHabValuesClassName;
            if (string.IsNullOrWhiteSpace(className))
            {
                LogErrorAndTerminate("OpenHAB values code generation class name is not specified in the KNX system configuration. Please check your configuration.");
                return;
            }

            var codeGen = new OpenHabValuesCodeGenerator(serviceProvider.GetRequiredService<ILogger<OpenHabValuesCodeGenerator>>());
            var code = codeGen.Generate(nonKnxItems, className, nameSpace);

            File.WriteAllText(filePath, code, System.Text.Encoding.UTF8);
            logger.LogInformation("Generated OpenHabValues source with {count} IValues for OpenHAB items and wrote to '{file}'",
                nonKnxItems.Count,
                filePath);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (cmd.ListItems)
            {
                var items = await openhabRestApiClient.GetItemsAsync(stoppingToken);
                foreach (var item in items)
                {
                    //logger.LogInformation("Item: {itemName}, Type: {itemType}, State: {itemState}", item.Name, item.Type, item.State);
                    Console.WriteLine($"Item: {item.Name}, Type: {item.Type}, State: {item.State.Clamp(100)}");
                }
                applicationLifetime.StopApplication();
            }
            if (cmd.UpdateHomeCompanionConfiguration)
            {
                await GenerateOpenHabValuesCodeAsync(stoppingToken);
                applicationLifetime.StopApplication();
            }
            else if (cmd.LogEvents)
            {
                logger.LogInformation("Logging events is not implemented yet.");
                applicationLifetime.StopApplication();
            }
            else
            {
                logger.LogInformation("No action specified. Use --help for more information.");
                applicationLifetime.StopApplication();
            }
        }
    }
}

public static class OpenHabExtensions
{
    public static string Clamp(this string value, int maxLength, bool addEllipsis = true)
    {
        var l = value.Length;
        return l <= maxLength ? value : value[.. Math.Min(l, maxLength)] + (addEllipsis ? "..." : "");
    }
}