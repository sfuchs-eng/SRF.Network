using FSDK = Knx.Falcon.Sdk;
using SRF.Network.Knx.Messages;
using F = Knx.Falcon;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Knx.Falcon;

public class SrfKnxBus : IKnxBus
{
    private readonly FSDK.KnxBus _knxBus;
    private readonly ILogger<SrfKnxBus> _logger;
    private readonly TimeProvider _timeProvider;

    public SrfKnxBus(FSDK.KnxBus knxBus, ILogger<SrfKnxBus> logger, TimeProvider timeProvider)
    {
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
        _knxBus       = knxBus       ?? throw new ArgumentNullException(nameof(knxBus));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _knxBus.ConnectionStateChanged += OnConnectionStateChanged;
        _knxBus.GroupMessageReceived += OnGroupMessageReceived;
    }

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

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _knxBus.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting from KNX bus...");
        return Task.CompletedTask;
    }

    public async Task SendGroupMessageAsync(IKnxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var destination = new F.GroupAddress(message.DestinationAddress.Address);
        var value = new F.GroupValue(message.Value.Value);
        var priority = message.Priority switch
        {
            MessagePriority.System => F.MessagePriority.System,
            MessagePriority.Normal => F.MessagePriority.High,
            MessagePriority.Alarm  => F.MessagePriority.Alarm,
            MessagePriority.Low    => F.MessagePriority.Low,
            _                      => F.MessagePriority.Low,
        };

        switch (message.EventType)
        {
            case GroupEventType.ValueWrite:
                await _knxBus.WriteGroupValueAsync(destination, value, priority, cancellationToken);
                break;
            case GroupEventType.ValueRead:
                await _knxBus.RequestGroupValueAsync(destination, priority, cancellationToken);
                break;
            case GroupEventType.ValueResponse:
                await _knxBus.RespondGroupValueAsync(destination, value, priority, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported GroupEventType: {message.EventType}");
        }
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
        MessageReceived?.Invoke(sender, new KnxMessageReceivedEventArgs(groupEventArgs, _timeProvider.GetUtcNow()));
    }
}