using Microsoft.Extensions.Hosting;

namespace SRF.Network.Udp.Hosting;

/// <summary>
/// Background service that manages a named UDP multicast connection and drains the
/// <see cref="UdpMessageQueue"/> with automatic reconnection and retry logic.
/// Consumers do not inject this service directly — inject
/// <c>[FromKeyedServices(name)] IUdpMessageQueue</c> to enqueue messages and
/// <c>[FromKeyedServices(name)] IUdpMulticastClient</c> to receive messages.
/// </summary>
public class UdpConnectionManager : BackgroundService
{
    private readonly string _name;
    private readonly UdpMessageQueue _queue;
    private readonly UdpConnectionManagerOptions _options;
    private readonly ILogger<UdpConnectionManager> _logger;
    private CancellationTokenSource? _operationsCts;
    private Task? _connectionTask;
    private Task? _sendingTask;

    // Convenience accessor — the client lives on the queue singleton.
    private IUdpMulticastClient Client => _queue.Client;

    public UdpConnectionManager(
        string name,
        UdpMessageQueue queue,
        IOptions<UdpConnectionManagerOptions> options,
        ILogger<UdpConnectionManager> logger)
    {
        _name    = name    ?? throw new ArgumentNullException(nameof(name));
        _queue   = queue   ?? throw new ArgumentNullException(nameof(queue));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[{ConnectionName}] UDP Connection Manager starting", _name);

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
            _logger.LogWarning("[{ConnectionName}] UDP auto-connect is disabled by configuration", _name);
            // Just wait for cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("UDP Connection Manager stopped");
    }

    private async Task ConnectionRunner(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[{ConnectionName}] UDP Connection Runner starting", _name);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!Client.IsConnected)
                {
                    _logger.LogInformation("[{ConnectionName}] UDP client not connected, attempting connection...", _name);
                    await TryConnectAsync(cancellationToken);
                }
                else
                {
                    _logger.LogTrace("[{ConnectionName}] UDP client is connected", _name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{ConnectionName}] Error in UDP connection runner", _name);
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectInterval), cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("[{ConnectionName}] UDP Connection Runner stopped", _name);
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Client.ConnectAsync(cancellationToken);

            if (Client.IsConnected)
                _logger.LogInformation("[{ConnectionName}] UDP client connected successfully", _name);
            else
                _logger.LogWarning("[{ConnectionName}] UDP connection attempt completed but client is not connected", _name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ConnectionName}] Failed to connect UDP client", _name);
        }
    }

    private async Task SendingRunner(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[{ConnectionName}] UDP Sending Runner starting", _name);

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
                    _logger.LogDebug("[{ConnectionName}] UDP client disconnected, requeueing message and waiting for connection", _name);
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
                        _logger.LogDebug("[{ConnectionName}] Requeueing failed message (attempt {Attempts}/{MaxAttempts})",
                            _name, item.Attempts, _options.MaxSendAttempts);
                        _queue.Requeue(item);
                        await Task.Delay(TimeSpan.FromSeconds(_options.SendRetryInterval), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning("[{ConnectionName}] Message failed after {Attempts} attempts, giving up", _name, item.Attempts);
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
                _logger.LogError(ex, "[{ConnectionName}] Unexpected error in UDP sending runner", _name);
            }
        }

        _logger.LogDebug("[{ConnectionName}] UDP Sending Runner stopped", _name);
    }

    private async Task<bool> TrySendAsync(UdpQueueItem item, CancellationToken cancellationToken)
    {
        item.Attempts++;

        try
        {
            await Client.SendAsync(item.Data, cancellationToken);
            _logger.LogTrace("[{ConnectionName}] Sent UDP message with {ByteCount} bytes (attempt {Attempts})",
                _name, item.Data.Length, item.Attempts);
            item.NotifySent();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "[{ConnectionName}] Cannot send UDP message - client not connected", _name);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ConnectionName}] Failed to send UDP message (attempt {Attempts})", _name, item.Attempts);
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{ConnectionName}] Stopping UDP Connection Manager", _name);

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
        _logger.LogInformation("[{ConnectionName}] UDP Connection Manager stopped", _name);
    }

    public override void Dispose()
    {
        _operationsCts?.Dispose();
        base.Dispose();
    }
}
