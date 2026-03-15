using Microsoft.Extensions.Hosting;

namespace SRF.Network.Udp.Hosting;

/// <summary>
/// Background service that manages UDP multicast connectivity and drains the
/// <see cref="UdpMessageQueue"/> with automatic reconnection and retry logic.
/// Consumers do not inject this service directly — inject <see cref="IUdpMessageQueue"/>
/// to enqueue messages and <see cref="IUdpMulticastClient"/> to receive messages.
/// </summary>
public class UdpConnectionManager : BackgroundService
{
    private readonly UdpMessageQueue _queue;
    private readonly UdpConnectionManagerOptions _options;
    private readonly ILogger<UdpConnectionManager> _logger;
    private CancellationTokenSource? _operationsCts;
    private Task? _connectionTask;
    private Task? _sendingTask;

    // Convenience accessor — the client lives on the queue singleton.
    private IUdpMulticastClient Client => _queue.Client;

    public UdpConnectionManager(
        UdpMessageQueue queue,
        IOptions<UdpConnectionManagerOptions> options,
        ILogger<UdpConnectionManager> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UDP Connection Manager starting");

        _operationsCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        if (_options.AutoConnect)
        {
            // Start connection management and sending loops
            _connectionTask = Task.Run(() => ConnectionRunner(_operationsCts.Token), stoppingToken);
            _sendingTask = Task.Run(() => SendingRunner(_operationsCts.Token), stoppingToken);

            // Wait for both tasks to complete
            await Task.WhenAll(_connectionTask, _sendingTask).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning("UDP auto-connect is disabled by configuration");
            // Just wait for cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("UDP Connection Manager stopped");
    }

    private async Task ConnectionRunner(CancellationToken cancellationToken)
    {
        _logger.LogDebug("UDP Connection Runner starting");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!Client.IsConnected)
                {
                    _logger.LogInformation("UDP client not connected, attempting connection...");
                    await TryConnectAsync(cancellationToken);
                }
                else
                {
                    _logger.LogTrace("UDP client is connected");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in UDP connection runner");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectInterval), cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("UDP Connection Runner stopped");
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Client.ConnectAsync(cancellationToken);

            if (Client.IsConnected)
                _logger.LogInformation("UDP client connected successfully");
            else
                _logger.LogWarning("UDP connection attempt completed but client is not connected");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect UDP client");
        }
    }

    private async Task SendingRunner(CancellationToken cancellationToken)
    {
        _logger.LogDebug("UDP Sending Runner starting");

        while (!_queue.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpQueueItem item;
                try
                {
                    item = _queue.Take(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Check if connected — requeue and wait rather than dropping the message
                if (!Client.IsConnected)
                {
                    _logger.LogDebug("UDP client disconnected, requeueing message and waiting for connection");
                    _queue.Requeue(item);
                    await Task.Delay(TimeSpan.FromSeconds(_options.SendRetryInterval), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Attempt to send
                bool sent = await TrySendAsync(item, cancellationToken);

                if (!sent)
                {
                    // Check if we should retry
                    if (item.Attempts < _options.MaxSendAttempts)
                    {
                        _logger.LogDebug("Requeueing failed message (attempt {Attempts}/{MaxAttempts})",
                            item.Attempts, _options.MaxSendAttempts);
                        _queue.Requeue(item);
                        await Task.Delay(TimeSpan.FromSeconds(_options.SendRetryInterval), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("Message failed after {Attempts} attempts, giving up", item.Attempts);
                        item.NotifyFailed($"Failed after {item.Attempts} attempts");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UDP sending runner");
            }
        }

        _logger.LogDebug("UDP Sending Runner stopped");
    }

    private async Task<bool> TrySendAsync(UdpQueueItem item, CancellationToken cancellationToken)
    {
        item.Attempts++;

        try
        {
            await Client.SendAsync(item.Data, cancellationToken);
            _logger.LogTrace("Sent UDP message with {ByteCount} bytes (attempt {Attempts})",
                item.Data.Length, item.Attempts);
            item.NotifySent();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Cannot send UDP message - client not connected");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send UDP message (attempt {Attempts})", item.Attempts);
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping UDP Connection Manager");

        // Signal all operations to stop
        _operationsCts?.Cancel();

        // Signal the queue that no more items will be added
        _queue.CompleteAdding();

        // Wait for background tasks to complete
        if (_connectionTask != null)
            await _connectionTask.ConfigureAwait(false);

        if (_sendingTask != null)
            await _sendingTask.ConfigureAwait(false);

        // Disconnect the underlying client
        if (Client.IsConnected)
            await Client.DisconnectAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("UDP Connection Manager stopped");
    }

    public override void Dispose()
    {
        _operationsCts?.Dispose();
        base.Dispose();
    }
}
