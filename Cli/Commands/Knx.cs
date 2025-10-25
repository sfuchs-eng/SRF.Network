using System;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Knx.Config.Domain;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;

namespace SRF.Network.Cli.Commands;

[CliCommand(Description = "KNX related functions.", Parent = typeof(Root))]
public class Knx : HostLauncher<Knx.Worker>
{
    [CliOption(Alias = "s", Description = "Scan for IP endpoints, then exit.")]
    public bool Scan { get; set; } = false;

    [CliOption(Alias = "l", Description = "Listen for incoming messages and log them to the console.")]
    public bool Listen { get; set; } = false;

    [CliOption(Description = "Load domain configuration and display...")]
    public bool Configuration { get; set; } = false;

    protected override void AddServices(IServiceCollection services, CliContext cliContext)
    {
        base.AddServices(services, cliContext);
        services.AddKnx<KnxConnection>();
    }

    public class Worker(
        Knx cmd,
        IKnxConnection knxConnection,
        IHostApplicationLifetime applicationLifetime,
        IServiceProvider serviceProvider,
        ILogger<Knx.Worker> logger
    ) : BackgroundService
    {
        private readonly Knx cmd = cmd;
        private readonly IKnxConnection knxConnection = knxConnection;
        private readonly IHostApplicationLifetime applicationLifetime = applicationLifetime;
        private readonly IServiceProvider serviceProvider = serviceProvider;
        private readonly ILogger<Worker> logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (cmd.Scan)
            {
                Console.WriteLine("Scanning for KNX Net/IP devices...");
                await foreach (var ep in KnxConnection.DiscoverKnxIpDevicesAsync(stoppingToken))
                {
                    Console.WriteLine("- {0}: {1}, {2}, {3}, {4}",
                            ep.FriendlyName,
                            ep.LocalIPAddress,
                            ep.MediumType.ToString(),
                            ep.MediumStatus,
                            ep.NetworkAdapterInfo.Name
                        );
                }
                Console.WriteLine("Scan completed.");
                applicationLifetime.StopApplication();
                return;
            }

            if (cmd.Listen)
            {
                knxConnection.Connect();
                knxConnection.MessageReceived += KnxMessageReceivedHandler;
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            if ( cmd.Configuration )
            {
                //var dc = serviceProvider.GetRequiredService<IDomainConfigurationFactory>().Load();
                var dc = serviceProvider.GetRequiredService<DomainConfiguration>();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    NoEtsGA = dc.GroupAddresses.Count
                }));
            }

            applicationLifetime.StopApplication();
        }

        private void KnxMessageReceivedHandler(object? sender, KnxMessageReceivedEventArgs e)
        {
            var tgtAddr = e.KnxMessageContext.GroupEventArgs?.DestinationAddress.ToString(); //.Address.To3LGroupAddress();
            var srcAddr = e.KnxMessageContext.GroupEventArgs?.SourceAddress.ToString(); //.FullAddress.To3LIndividualAddress();
            var payload = string.Join(',', e.KnxMessageContext.GroupEventArgs?.Value.Value.Select(b => $"0x{b.ToString("X2")}") ?? ["no payload"]);
            Console.WriteLine($"- from {srcAddr} to {tgtAddr}: {payload}");
        }
    }
}
