using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Industrial.Packets;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Knx.Messages;
using SRF.Network.Udp;

namespace SRF.Network.Knx.Connection;

/// <summary>
/// <see cref="IKnxBus"/> implementation that uses UDP multicast as the transport layer,
/// encoding and decoding KNX/IP Routing Indication frames (service type 0x0530) with
/// standard cEMI L_DATA framing.
/// <para>
/// The local KNX individual address used as the source address in outbound frames is read
/// from <see cref="KnxConfiguration.ConnectionString"/> using the Falcon SDK connection-string
/// syntax (semicolon-separated key=value pairs). Example:
/// <c>"Type=IpRouting;KnxAddress=1.1.5"</c>. Defaults to <c>0.0.1</c> if the key is absent.
/// </para>
/// </summary>
public class KnxIpRoutingBus : IKnxBus
{
    private readonly IUdpMulticastClient _udpClient;
    private readonly IKnxIpRoutingQueue _sendQueue;
    private readonly KnxIpRoutingOptions _routingOptions;
    private readonly ILogger<KnxIpRoutingBus> _logger;
    private readonly IndividualAddress _localAddress;
    private readonly KnxIpRoutingPayloadProvider _payloadProvider = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="KnxIpRoutingBus"/>.
    /// </summary>
    public KnxIpRoutingBus(
        IUdpMulticastClient udpClient,
        IKnxIpRoutingQueue sendQueue,
        IOptions<KnxConfiguration> options,
        IOptions<KnxIpRoutingOptions> routingOptions,
        ILogger<KnxIpRoutingBus> logger,
        TimeProvider timeProvider)
    {
        _udpClient      = udpClient  ?? throw new ArgumentNullException(nameof(udpClient));
        _sendQueue      = sendQueue  ?? throw new ArgumentNullException(nameof(sendQueue));
        _logger         = logger     ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider   = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _routingOptions = routingOptions?.Value ?? new KnxIpRoutingOptions();

        _localAddress = ParseKnxAddress(options?.Value?.ConnectionString);

        _udpClient.ConnectionStatusChanged += OnUdpConnectionStatusChanged;
    }

    /// <inheritdoc/>
    public bool IsConnected => _udpClient.IsConnected;

    /// <inheritdoc/>
    public BusConnectionState ConnectionState =>
        _udpClient.IsConnected ? BusConnectionState.Connected : BusConnectionState.Closed;

    /// <inheritdoc/>
    public event EventHandler<Knx.KnxConnectionEventArgs> ConnectionStateChanged = delegate { };
    /// <inheritdoc/>
    public event EventHandler<KnxMessageReceivedEventArgs> MessageReceived = delegate { };

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _udpClient.MessageReceived += OnUdpMessageReceived;
        await _udpClient.ConnectAsync(cancellationToken);
        _logger.LogInformation("KNX/IP routing bus connected (local address {LocalAddress}).", _localAddress);
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _udpClient.MessageReceived -= OnUdpMessageReceived;
        await _udpClient.DisconnectAsync(cancellationToken);
        _logger.LogInformation("KNX/IP routing bus disconnected.");
    }

    /// <inheritdoc/>
    public Task SendGroupMessageAsync(IKnxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Encode priority into Ctrl1 bits [3:2] — KNX wire encoding matches MessagePriority values exactly.
        byte ctrl1 = (byte)((0xBC & ~0x0C) | ((int)message.Priority << 2));

        var frame = new CemiLDataFrame
        {
            MessageCode        = CemiLDataFrame.MessageCodeReq,
            Ctrl1              = ctrl1,
            SourceAddress      = _localAddress,
            DestinationAddress = message.DestinationAddress,
            EventType          = message.EventType,
            Value              = message.Value,
        };

        var header = new KnxIpHeader { Payload = frame };
        int size = header.Measure();

        using var buf = new PacketBuffer(size, endianSwap: false); // encode writes its own SwappingBinaryWriter
        header.Encode(buf.Writer);
        buf.Writer.Flush();

        byte[] bytes = new byte[size];
        Array.Copy(buf.Buffer, bytes, size);

        int bits = _routingOptions.ComputeTelegramBits(size);
        _sendQueue.Enqueue(bytes, bits);

        _logger.LogTrace("Enqueued KNX group message: {EventType} → {DestinationAddress} ({ValueHex})",
            message.EventType, message.DestinationAddress, Convert.ToHexString(message.Value.Value));

        return Task.CompletedTask;
    }

    private void OnUdpMessageReceived(object? sender, UdpMessageReceivedEventArgs e)
    {
        try
        {
            var header = new KnxIpHeader();
            using var ms = new MemoryStream(e.Data);
            using var reader = new BinaryReader(ms);
            header.Decode(reader, [_payloadProvider]);

            if (header.Payload is not CemiLDataFrame frame)
            {
                _logger.LogTrace("Received KNX/IP frame with unsupported service type 0x{ServiceType:X4}, ignoring.", header.ServiceType);
                return;
            }

            var groupEventArgs = new GroupEventArgs
            {
                DestinationAddress = frame.DestinationAddress,
                SourceAddress      = frame.SourceAddress,
                EventType          = frame.EventType,
                Value              = frame.Value,
                // Decode priority from Ctrl1 bits [3:2] — KNX wire encoding matches MessagePriority values exactly.
                Priority           = (MessagePriority)((frame.Ctrl1 >> 2) & 0x03),
            };

            _logger.LogTrace("Received KNX group message: {EventType} ← {SourceAddress} → {DestinationAddress} ({ValueHex})",
                frame.EventType, frame.SourceAddress, frame.DestinationAddress, Convert.ToHexString(frame.Value.Value));

            _sendQueue.NotifyReceived(_routingOptions.ComputeTelegramBits(e.Data.Length));
            MessageReceived.Invoke(this, new KnxMessageReceivedEventArgs(groupEventArgs, _timeProvider.GetUtcNow()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode incoming KNX/IP routing frame ({ByteCount} bytes).", e.Data.Length);
        }
    }

    private void OnUdpConnectionStatusChanged(object? sender, UdpConnectionEventArgs e)
    {
        _logger.LogInformation("KNX/IP routing bus connection status: {IsConnected}{Error}",
            e.IsConnected,
            e.ErrorMessage != null ? $" — {e.ErrorMessage}" : string.Empty);
        ConnectionStateChanged?.Invoke(this, new Knx.KnxConnectionEventArgs());
    }

    /// <summary>
    /// Parses the <c>KnxAddress</c> token from a Falcon SDK connection string
    /// (<c>"Type=IpRouting;KnxAddress=1.1.5"</c>).
    /// Returns <c>0.0.1</c> if the token is absent or cannot be parsed.
    /// </summary>
    private static IndividualAddress ParseKnxAddress(string? connectionString)
    {
        const string fallback = "0.0.1";

        if (string.IsNullOrWhiteSpace(connectionString))
            return new IndividualAddress(fallback);

        foreach (var token in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = token.IndexOf('=');
            if (idx < 0) continue;
            var key = token[..idx].Trim();
            var val = token[(idx + 1)..].Trim();
            if (key.Equals("KnxAddress", StringComparison.OrdinalIgnoreCase))
            {
                try { return new IndividualAddress(val); }
                catch { break; }
            }
        }

        return new IndividualAddress(fallback);
    }
}
