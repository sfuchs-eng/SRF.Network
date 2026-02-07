using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Network.Knx;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx.Connection;

public class KnxConnection : IKnxConnection
{
    private readonly KnxConfiguration config;
    private readonly ILogger<KnxConnection> logger;
    private readonly IKnxBus knxBus;

    public bool IsConnected { get => knxBus.IsConnected; }

    public event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;

    public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

    public KnxConnection(
        IKnxLibraryInitialization knxLibInitializer, // must be constructed before any other Knx.Falcon class is instanciated.
        IKnxBus knxBus,
        IOptions<KnxConfiguration> options,
        ILogger<KnxConnection> logger)
    {
        this.config = options.Value;
        this.logger = logger;

        this.knxBus = knxBus;

        /* with Falcon...
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
        */

        knxBus.ConnectionStateChanged += (s, e) => { OnConnectionStatusChanged(e); };
    }

    public async Task ConnectAsync()
    {
        if (!IsConnected)
        {
            await knxBus.ConnectAsync();
            if (knxBus.ConnectionState == BusConnectionState.Connected)
            {
                logger.LogInformation("KNX bus connected.");
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

    protected virtual void OnConnectionStatusChanged(KnxConnectionEventArgs e)
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
