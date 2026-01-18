using System.Text.Json;
using Knx.Falcon;
using Knx.Falcon.DataSecurity;
using Knx.Falcon.Discovery;
using Knx.Falcon.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Network.Knx.FalconSupport;

namespace SRF.Network.Knx.Connection;

public class KnxConnection : IKnxConnection
{
    private readonly KnxConfiguration config;
    private readonly ILogger<KnxConnection> logger;
    private readonly KnxBus knxBus;

    public bool IsConnected { get => knxBus.ConnectionState == BusConnectionState.Connected; }

    public event EventHandler<KnxConnectionEventArgs>? ConnectionStatusChanged;

    public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

    public KnxConnection(
        FalconInitializer falconInitializer, // must be constructed before any other Knx.Falcon class is instanciated.
        IOptions<KnxConfiguration> options,
        ILogger<KnxConnection> logger)
    {
        this.config = options.Value;
        this.logger = logger;

        knxBus = new KnxBus(config.ConnectionString);
        if (config.CommSecurity.UseCommSecurity)
        {
            if (!File.Exists(config.CommSecurity.KeyRingFile))
                throw new FileNotFoundException("The specified KNX key ring file does not exist.", config.CommSecurity.KeyRingFile);

            var sec = GroupCommunicationSecurity.Load(config.CommSecurity.KeyRingFile,
                config.CommSecurity.KeyRingPassword ?? throw new KnxException("The KNX key ring password must be set when using KNX/IP with communication security."));

            if (string.IsNullOrEmpty(config.CommSecurity.SequenceControlFile) || !File.Exists(config.CommSecurity.SequenceControlFile))
                throw new FileNotFoundException("No KNX sequence counter file specified or it does not exist.", config.CommSecurity.SequenceControlFile);
            sec.LoadDeviceSequenceCounters(config.CommSecurity.SequenceControlFile,
                config.CommSecurity.SequenceControlPassword ?? throw new KnxException("The KNX sequence counter password must be set when using KNX/IP with communication security."));

            knxBus.GroupCommunicationSecurity = sec;
            logger.LogInformation("KNX communication security enabled.");
        }

        knxBus.ConnectionStateChanged += (s, e) => { OnConnectionStatusChanged(e); };
    }

    /// <summary>
    /// If no cancellation token is provided, there's an internal 60s timeout token being generated.
    /// </summary>
    public static IAsyncEnumerable<IpDeviceDiscoveryResult> DiscoverKnxIpDevicesAsync(CancellationToken? token = null)
    {
        var discovery = new IpDeviceDiscovery() { UseExtendedSearch = true, UseV1Search = true };
        return discovery.DiscoverAsync(token ?? new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    }

    public async Task ConnectAsync()
    {
        if (!IsConnected)
        {
            await knxBus.ConnectAsync();
            if (knxBus.ConnectionState == BusConnectionState.Connected)
            {
                logger.LogInformation("KNX bus connected with type {connectorType}", knxBus.ConnectorType);
                logger.LogDebug("KNX connection config: {interfaceConfig}",
                    JsonSerializer.Serialize(knxBus.InterfaceConfiguration, knxBus.InterfaceConfiguration.GetType()));
                logger.LogDebug("KNX connection features: {interfaceFeatures}",
                    JsonSerializer.Serialize(knxBus.InterfaceFeatures, knxBus.InterfaceFeatures.GetType()));
                knxBus.GroupMessageReceived += OnGroupMessageReceived;
            //knxBus.IoTGroupMessageReceived += OnIoTGroupMessageReceived;
            }
            else
                logger.LogError("KNX bus connection failed, left in status {connectionStatus}", knxBus.ConnectionState);
        }
    }

    public async Task DisconnectAsync()
    {
        knxBus.GroupMessageReceived -= OnGroupMessageReceived;
        await Task.CompletedTask;
        //knxBus.IoTGroupMessageReceived -= OnIoTGroupMessageReceived;
    }

    public async Task SendMessageAsync(IKnxMessage message, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    protected virtual void OnConnectionStatusChanged(EventArgs e)
    {
        logger.LogTrace("Knx connection status changed to {connStatus}", knxBus.ConnectionState);
        ConnectionStatusChanged?.Invoke(this, new KnxConnectionEventArgs
        {
        });
    }

    private void OnGroupMessageReceived(object? sender, GroupEventArgs e)
    {
        try
        {
            MessageReceived?.Invoke(this, new KnxMessageReceivedEventArgs(e));
        }
        catch ( Exception ex )
        {
            logger.LogWarning(ex, "Failed to process KNX Group Message: {groupAddress} from {sourceAddress}",
                e.DestinationAddress.Address.To3LGroupAddress(),
                e.SourceAddress.FullAddress.To3LIndividualAddress());
        }
    }

    private void OnIoTGroupMessageReceived(object? sender, IoTGroupEventArgs e)
    {
        logger.LogWarning("{methodName} for handling IoT group messages is not implemented yet.", nameof(OnIoTGroupMessageReceived));
    }
}
