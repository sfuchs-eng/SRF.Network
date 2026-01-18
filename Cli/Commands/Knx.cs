using System;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    [CliOption(Alias = "f", Description = "Filter group addresses for listening (comma-separated list of 3-level addresses in format 3/4/5). If not provided, all messages are logged.")]
    public string? GroupAddressFilter { get; set; }

    [CliOption(Description = "Load domain configuration and display...")]
    public bool Configuration { get; set; } = false;

    [CliOption(Alias = "kc", Description = "Print the KNX related program configuration")]
    public bool PrintKnxConfiguration { get; set; } = false;

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
        private HashSet<string>? allowedGroupAddresses = null;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool didSomething = false;

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
                // Parse the group address filter if provided
                if (!string.IsNullOrWhiteSpace(cmd.GroupAddressFilter))
                {
                    allowedGroupAddresses = new HashSet<string>(
                        cmd.GroupAddressFilter.Split(',')
                            .Select(addr => addr.Trim())
                            .Where(addr => !string.IsNullOrWhiteSpace(addr))
                    );
                    Console.WriteLine($"Listening to group addresses: {string.Join(", ", allowedGroupAddresses)}");
                }
                else
                {
                    Console.WriteLine("Listening to all group addresses...");
                }

                knxConnection.Connect();
                knxConnection.MessageReceived += KnxMessageReceivedHandler;
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                didSomething = true;
            }

            if ( cmd.Configuration )
            {
                //var dc = serviceProvider.GetRequiredService<IDomainConfigurationFactory>().Load();
                var dc = serviceProvider.GetRequiredService<DomainConfiguration>();

                cmd.JsonOutput(new
                {
                    NoEtsGA = dc.GroupAddresses.Count,
                    DomainConfig = dc,
                    EtsGaWithoutDpt = dc.GroupAddresses.Values.Where(ga => !ga.HasValidDPT),
                });
                didSomething = true;
            }

            if (!didSomething || cmd.PrintKnxConfiguration)
            {
                var kc = serviceProvider.GetRequiredService<IOptions<SRF.Knx.Config.KnxConfiguration>>();
                cmd.JsonOutput(new { Knx = kc.Value });
                didSomething = true;
            }
            
            applicationLifetime.StopApplication();
        }

        private void KnxMessageReceivedHandler(object? sender, KnxMessageReceivedEventArgs e)
        {
            var tgtAddr = e.KnxMessageContext.GroupEventArgs?.DestinationAddress.ToString(); //.Address.To3LGroupAddress();
            var srcAddr = e.KnxMessageContext.GroupEventArgs?.SourceAddress.ToString(); //.FullAddress.To3LIndividualAddress();
            
            // If a filter is set, check if the destination address matches
            if (allowedGroupAddresses != null)
            {
                if (tgtAddr == null || !allowedGroupAddresses.Contains(tgtAddr))
                {
                    return;
                }
            }
            
            var payload = string.Join(',', e.KnxMessageContext.GroupEventArgs?.Value.Value.Select(b => $"0x{b.ToString("X2")}") ?? ["no payload"]);
            Console.WriteLine($"- from {srcAddr} to {tgtAddr}: {payload}");
        }
    }
}
