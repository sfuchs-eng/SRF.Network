using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Core;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Knx.Connection;

/// <summary>
/// Represents a connection to a KNX bus, managing the connection state, handling incoming messages, and providing an interface for sending messages.
/// It uses <see cref="IKnxBus"/> for interacting with the underlying KNX bus and its connection protocols.
/// </summary>
public class KnxConnection : IKnxConnection
{
    private readonly KnxConfiguration config;
    private readonly ILogger<KnxConnection> logger;
    private readonly IKnxBus knxBus;
    private readonly IDptResolver _dptResolver;

    public bool IsConnected { get => knxBus.IsConnected; }

    public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<Knx.KnxConnectionEventArgs>? ConnectionStatusChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnxConnection"/> class.
    /// </summary>
    /// <param name="knxLibInitializer">The KNX library initializer. Allows to initialize the KNX library before any other KNX components are instantiated.</param>
    /// <param name="knxBus">The KNX bus instance.</param>
    /// <param name="options">The KNX configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dptResolver">Resolves the Data Point Type for incoming group addresses to enable payload decoding.</param>
    public KnxConnection(
        IKnxLibraryInitialization knxLibInitializer,
        IKnxBus knxBus,
        IOptions<KnxConfiguration> options,
        ILogger<KnxConnection> logger,
        IDptResolver dptResolver)
    {
        this.config = options.Value;
        this.logger = logger;
        this.knxBus = knxBus;
        this._dptResolver  = dptResolver  ?? throw new ArgumentNullException(nameof(dptResolver));

        /* with Falcon... which is meanwhile removed and put into a separate package/project.
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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            await knxBus.ConnectAsync(cancellationToken);
            if (knxBus.ConnectionState == BusConnectionState.Connected)
            {
                logger.LogInformation("KNX bus connected.");
                knxBus.MessageReceived += OnBusMessageReceived;
                //knxBus.IoTGroupMessageReceived += OnIoTGroupMessageReceived;
            }
            else
                logger.LogError("KNX bus connection failed, left in status {connectionStatus}", knxBus.ConnectionState);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        knxBus.MessageReceived -= OnBusMessageReceived;
        await knxBus.DisconnectAsync(cancellationToken);
        //knxBus.IoTGroupMessageReceived -= OnIoTGroupMessageReceived;
    }

    public async Task SendMessageAsync(IKnxMessage message, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);

        await knxBus.SendGroupMessageAsync(message, token);
    }

    protected virtual void OnConnectionStatusChanged(Knx.KnxConnectionEventArgs e)
    {
        logger.LogTrace("Knx connection status changed to {connStatus}", knxBus.ConnectionState);
        ConnectionStatusChanged?.Invoke(this, new Knx.KnxConnectionEventArgs
        {
        });
    }

    protected virtual void OnBusMessageReceived(object? sender, KnxMessageReceivedEventArgs e)
    {
        try
        {
            var ctx = e.KnxMessageContext;

            try
            {
                var dpt = _dptResolver.GetDpt(ctx.GroupEventArgs!.DestinationAddress);
                ctx.Dpt          = dpt;
                ctx.DecodedValue = dpt.ToValue(ctx.GroupEventArgs.Value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decode DPT value for group address {GroupAddress}.", ctx.GroupEventArgs?.DestinationAddress);
            }

            MessageReceived?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process KNX Group Message: {groupAddress} from {sourceAddress}",
                e.KnxMessageContext.GroupEventArgs?.DestinationAddress?.ToString(),
                e.KnxMessageContext.GroupEventArgs?.SourceAddress?.ToString());
        }
    }

    /*
        private void OnIoTGroupMessageReceived(object? sender, IoTGroupEventArgs e)
        {
            logger.LogWarning("{methodName} for handling IoT group messages is not implemented yet.", nameof(OnIoTGroupMessageReceived));
        }
        */
}
