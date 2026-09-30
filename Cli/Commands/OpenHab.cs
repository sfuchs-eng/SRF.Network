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
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Cli.Commands;

[CliCommand(Description = "OpenHAB related functions.", Parent = typeof(Root))]
public class OpenHab : HostLauncher<OpenHab.Worker>
{
    [CliOption(Alias = "i", Description = "List items")]
    public bool ListItems { get; set; } = false;

    [CliOption(Alias = "l", Description = "Connect to OpenHAB and log all events to the console.")]
    public bool LogEvents { get; set; } = false;

    [CliOption(Alias = "hc", Description = "Update HomeCompanion configuration from OpenHAB items -- NOT IMPL here, moved to HomeCompanion.Cli = hccli")]
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
            IEventBusClient eventBusClient,
            IHostApplicationLifetime applicationLifetime,
            ILogger<Worker> logger,
            IServiceProvider serviceProvider
        ) : BackgroundService
    {
        private readonly OpenHab cmd = cmd;
        private readonly IRestApiClient openhabRestApiClient = openhabRestApiClient;
        private readonly IEventBusClient eventBusClient = eventBusClient;
        private readonly IHostApplicationLifetime applicationLifetime = applicationLifetime;
        private readonly ILogger<Worker> logger = logger;
        private readonly IServiceProvider serviceProvider = serviceProvider;

        private void LogErrorAndTerminate(string message)
        {
            logger.LogError(message);
            applicationLifetime.StopApplication();
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
                return;
            }

            if (cmd.UpdateHomeCompanionConfiguration)
            {
                logger.LogError("Function got moved to HomeCompanion.Cli, see `hccli ohvcg`");
                applicationLifetime.StopApplication();
                return;
            }

            if (cmd.LogEvents)
            {
                EventHandler<EventReceivedEventArgs> onEvent = (_, e) =>
                {
                    if (e.Received is not ItemEvent && e.Received is not ItemStateUpdatedEvent)
                        return;

                    var type = e.Received.Type;
                    var source = e.Received.Source ?? "<none>";
                    Console.WriteLine($"[{e.When:O}] {type}: topic={e.Received.Topic}, source={source}, payload={e.Received.PayloadJson.Clamp(200, true)}");
                };

                eventBusClient.EventReceived += onEvent;
                logger.LogInformation("Logging OpenHAB websocket events. Press Ctrl+C to stop.");

                try
                {
                    // Connection lifecycle is managed by OpenHabConnector hosted service.
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                finally
                {
                    eventBusClient.EventReceived -= onEvent;
                }

                applicationLifetime.StopApplication();
                return;
            }

            logger.LogInformation("No action specified. Use --help for more information.");
            applicationLifetime.StopApplication();
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