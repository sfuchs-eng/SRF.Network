using System;
using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;

namespace SRF.Network.Cli.Commands;

[CliCommand(Description = "OpenHAB related functions.", Parent = typeof(Root))]
public class OpenHab : HostLauncher<OpenHab.Worker>
{
    [CliOption(Alias = "i", Description = "List items")]
    public bool ListItems { get; set; } = false;

    [CliOption(Alias = "l", Description = "Connect to OpenHAB and log all events to the console.")]
    public bool LogEvents { get; set; } = false;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddOpenHabConnector();
    }

    public class Worker(
            OpenHab cmd,
            IHostApplicationLifetime applicationLifetime,
            ILogger<Worker> logger
        ) : BackgroundService
    {
        private readonly OpenHab cmd = cmd;
        private readonly IHostApplicationLifetime applicationLifetime = applicationLifetime;
        private readonly ILogger<Worker> logger = logger;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if ( cmd.ListItems )
            {
                logger.LogInformation("Listing items is not implemented yet.");
                applicationLifetime.StopApplication();
            }
            else if ( cmd.LogEvents )
            {
                logger.LogInformation("Logging events is not implemented yet.");
                applicationLifetime.StopApplication();
            }
            else
            {
                logger.LogInformation("No action specified. Use --help for more information.");
                applicationLifetime.StopApplication();
            }
            return Task.CompletedTask;
        }
    }
}
