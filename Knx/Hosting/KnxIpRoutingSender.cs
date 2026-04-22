using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Udp;

namespace SRF.Network.Knx.Hosting;

/// <summary>
/// Background service that drains the <see cref="KnxIpRoutingQueue"/>, applies rate limiting,
/// and sends each frame via the keyed <see cref="IUdpMulticastClient"/>.
/// <para>
/// Consumers do not inject this service directly. Inject <see cref="IKnxIpRoutingQueue"/>
/// to enqueue outbound frames and <see cref="IUdpMulticastClient"/> (keyed) to receive frames.
/// </para>
/// </summary>
public class KnxIpRoutingSender : BackgroundService
{
    private readonly string _name;
    private readonly KnxIpRoutingQueue _queue;
    private readonly IUdpMulticastClient _udpClient;
    private readonly ILogger<KnxIpRoutingSender> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="KnxIpRoutingSender"/>.
    /// </summary>
    public KnxIpRoutingSender(
        string name,
        KnxIpRoutingQueue queue,
        IUdpMulticastClient udpClient,
        ILogger<KnxIpRoutingSender> logger)
    {
        _name      = name      ?? throw new ArgumentNullException(nameof(name));
        _queue     = queue     ?? throw new ArgumentNullException(nameof(queue));
        _udpClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
        _logger    = logger    ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{ConnectionName}] KNX/IP Routing Sender starting.", _name);

        await Task.Run(() => SendingRunner(stoppingToken), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("[{ConnectionName}] KNX/IP Routing Sender stopped.", _name);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{ConnectionName}] Stopping KNX/IP Routing Sender.", _name);

        _queue.CompleteAdding();

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendingRunner(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[{ConnectionName}] KNX/IP Routing Sender loop starting.", _name);

        while (!_queue.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            KnxIpRoutingQueueItem item;
            try
            {
                item = _queue.Take(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                // Queue completed — normal shutdown.
                break;
            }

            try
            {
                // Apply rate limiting with the actual bit cost of this telegram.
                await _queue.WaitForSendSlotAsync(item.Bits, cancellationToken).ConfigureAwait(false);

                await _udpClient.SendAsync(item.Data, cancellationToken).ConfigureAwait(false);

                _logger.LogTrace("[{ConnectionName}] Sent KNX/IP frame ({Bytes} bytes, {Bits} bits).",
                    _name, item.Data.Length, item.Bits);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{ConnectionName}] Failed to send KNX/IP frame ({Bytes} bytes).",
                    _name, item.Data.Length);
                // Drop the frame — no retry to avoid re-ordering or stale data.
            }
        }

        _logger.LogDebug("[{ConnectionName}] KNX/IP Routing Sender loop stopped.", _name);
    }
}
