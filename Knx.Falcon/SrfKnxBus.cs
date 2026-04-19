using FSDK = Knx.Falcon.Sdk;
using SRF.Network.Knx.Messages;
using F = Knx.Falcon;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Knx.Falcon;

public class SrfKnxBus : IKnxBus
{
    private readonly FSDK.KnxBus _knxBus;
    private readonly ILogger<SrfKnxBus> _logger;

    public SrfKnxBus(FSDK.KnxBus knxBus, ILogger<SrfKnxBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _knxBus = knxBus ?? throw new ArgumentNullException(nameof(knxBus));
        _knxBus.ConnectionStateChanged += OnConnectionStateChanged;
        _knxBus.GroupMessageReceived += OnGroupMessageReceived;
        _knxBus.GroupMessageReceived += OnGroupMessageReceived;
    }

    public event EventHandler<GroupEventArgs>? GroupMessageReceived;

    public bool IsConnected => _knxBus.ConnectionState == F.BusConnectionState.Connected;

    public event EventHandler<KnxConnectionEventArgs>? ConnectionStateChanged;

    public event EventHandler<KnxMessageReceivedEventArgs>? MessageReceived;

    public Connection.BusConnectionState ConnectionState => _knxBus.ConnectionState switch
    {
        F.BusConnectionState.Connected => Connection.BusConnectionState.Connected,
        F.BusConnectionState.Broken => Connection.BusConnectionState.Broken,
        F.BusConnectionState.Closed => Connection.BusConnectionState.Closed,
        F.BusConnectionState.MediumFailure => Connection.BusConnectionState.MediumFailure,
        _ => throw new InvalidOperationException($"Unknown connection state: {_knxBus.ConnectionState}")
    };

    public Task ConnectAsync()
    {
        return _knxBus.ConnectAsync();
    }

    public Task DisconnectAsync()
    {
        _logger.LogInformation("Disconnecting from KNX bus...");
        return Task.CompletedTask;
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        var knxEventArgs = new KnxConnectionEventArgs();
        ConnectionStateChanged?.Invoke(sender, knxEventArgs);
    }

    private void OnGroupMessageReceived(object? sender, global::Knx.Falcon.GroupEventArgs e)
    {
        var groupEventArgs = new GroupEventArgs
        {
            EventType = e.EventType switch
            {
                global::Knx.Falcon.GroupEventType.ValueRead => GroupEventType.ValueRead,
                global::Knx.Falcon.GroupEventType.ValueResponse => GroupEventType.ValueResponse,
                global::Knx.Falcon.GroupEventType.ValueWrite => GroupEventType.ValueWrite,
                _ => throw new InvalidOperationException($"Unknown group event type: {e.EventType}")
            },
            SourceAddress = new SRF.Knx.Core.IndividualAddress(e.SourceAddress.FullAddress),
            DestinationAddress = new SRF.Knx.Core.GroupAddress(e.DestinationAddress.Address),
            Value = new SRF.Knx.Core.GroupValue(e.Value.Value)
        };
        GroupMessageReceived?.Invoke(sender, groupEventArgs);
    }
}