using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly TimeProvider _timeProvider;

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
    /// <param name="timeProvider">The time provider instance.</param>
    public KnxConnection(
        IKnxLibraryInitialization knxLibInitializer,
        IKnxBus knxBus,
        IOptions<KnxConfiguration> options,
        ILogger<KnxConnection> logger,
        TimeProvider timeProvider)
    {
        this.config = options.Value;
        this.logger = logger;
        this.knxBus = knxBus;
        this._timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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

    protected virtual void OnConnectionStatusChanged(Knx.KnxConnectionEventArgs e)
    {
        logger.LogTrace("Knx connection status changed to {connStatus}", knxBus.ConnectionState);
        ConnectionStatusChanged?.Invoke(this, new Knx.KnxConnectionEventArgs
        {
        });
    }

    protected virtual void OnGroupMessageReceived(object? sender, GroupEventArgs e)
    {
        try
        {
            var ctx = new KnxMessageContext(e, _timeProvider.GetUtcNow());
            MessageReceived?.Invoke(this, new KnxMessageReceivedEventArgs(ctx));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process KNX Group Message: {groupAddress} from {sourceAddress}",
                e.DestinationAddress.ToString(),
                e.SourceAddress.ToString());
        }
    }

    /*
        private void OnIoTGroupMessageReceived(object? sender, IoTGroupEventArgs e)
        {
            logger.LogWarning("{methodName} for handling IoT group messages is not implemented yet.", nameof(OnIoTGroupMessageReceived));
        }
        */
}
