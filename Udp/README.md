# SRF.Network.Udp

Utility library simplifying the use of UDP Multicast messaging and related configuration.
It's built on top of the .NET `System.Net.Sockets` namespace and provides a higher-level API for common UDP operations.

It's initially designed for use with the SRF.Network.Knx library, but should serve as a general-purpose utility for any UDP-based communication needs.

## Features

- Configure what interfaces to bind to and send from
- Configure multicast group membership and TTL
- Send and receive messages with a simple API
- Support for IPv4 (IPv6 support planned for future)
- Automatic handling of socket options for multicast communication
- Asynchronous send and receive operations
- Logging of socket operations and errors
- Support for cancellation and timeouts
- Dependency Injection (DI) support with IOptions pattern
- Event-driven message reception
- Hosting extensions for easy integration with .NET Generic Host

## Installation

Add the project reference to your project:

```bash
dotnet add reference path/to/SRF.Network.Udp.csproj
```

## Usage

### Basic Setup with Dependency Injection

#### 1. Configure in appsettings.json

```json
{
  "Udp": {
    "Multicast": {
      "MulticastAddress": "224.0.23.12",
      "Port": 3671,
      "LocalInterface": null,
      "TimeToLive": 16,
      "ReceiveBufferSize": 8192,
      "SendBufferSize": 8192,
      "ReuseAddress": true,
      "ReceiveTimeout": "00:00:05"
    }
  }
}
```

#### 2. Register Services

Using `IServiceCollection`:

```csharp
using SRF.Network.Udp.Hosting;

// In your Startup.cs or Program.cs
services.AddUdpMulticast(); // Uses default config section "Udp:Multicast"

// Or specify a custom configuration section
services.AddUdpMulticast("MyCustomSection");
```

Using `IHostBuilder`:

```csharp
Host.CreateDefaultBuilder(args)
    .AddUdpMulticast()
    .ConfigureServices((context, services) =>
    {
        // Other services...
    })
    .Build();
```

Using `IHostApplicationBuilder`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddUdpMulticast();
```

#### 3. Inject and Use

```csharp
using SRF.Network.Udp;

public class MyService
{
    private readonly IUdpMulticastClient _udpClient;
    private readonly ILogger<MyService> _logger;

    public MyService(IUdpMulticastClient udpClient, ILogger<MyService> logger)
    {
        _udpClient = udpClient;
        _logger = logger;

        // Subscribe to events
        _udpClient.MessageReceived += OnMessageReceived;
        _udpClient.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _udpClient.ConnectAsync(cancellationToken);
    }

    public async Task SendDataAsync(byte[] data)
    {
        if (_udpClient.IsConnected)
        {
            await _udpClient.SendAsync(data);
        }
    }

    private void OnMessageReceived(object? sender, UdpMessageReceivedEventArgs e)
    {
        _logger.LogInformation("Received {ByteCount} bytes from {RemoteEndPoint} at {ReceivedAt}",
            e.Data.Length, e.RemoteEndPoint, e.ReceivedAt);
        
        // Process the data
        ProcessMessage(e.Data);
    }

    private void OnConnectionStatusChanged(object? sender, UdpConnectionEventArgs e)
    {
        if (e.IsConnected)
        {
            _logger.LogInformation("UDP client connected");
        }
        else
        {
            _logger.LogWarning("UDP client disconnected: {ErrorMessage}", e.ErrorMessage);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _udpClient.DisconnectAsync(cancellationToken);
    }
}
```

### Automatic Connection Management

For production scenarios, use the `UdpConnectionManager` which provides automatic reconnection and queued message transmission with retry logic.

#### 1. Configure in appsettings.json

```json
{
  "Udp": {
    "Multicast": {
      "MulticastAddress": "224.0.23.12",
      "Port": 3671
    },
    "ConnectionManager": {
      "ReconnectInterval": 10.0,
      "SendRetryInterval": 5.0,
      "MaxSendAttempts": 3,
      "AutoConnect": true
    }
  }
}
```

#### 2. Register Services with Connection Manager

```csharp
using SRF.Network.Udp.Hosting;

// Register with automatic connection management
services.AddUdpMulticastWithConnectionManager();

// Or with custom configuration sections
services.AddUdpMulticastWithConnectionManager("Udp:Multicast", "Udp:ConnectionManager");
```

#### 3. Use with Message Queue

```csharp
using SRF.Network.Udp;

public class MyService
{
    private readonly IUdpMessageQueue _queue;
    private readonly ILogger<MyService> _logger;

    public MyService(IUdpMessageQueue queue, ILogger<MyService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public void SendMessage(byte[] data)
    {
        // Enqueue message - it will be sent automatically when connected
        var queueItem = _queue.Enqueue(data);

        // Optionally subscribe to send status events
        queueItem.Sent += (sender, e) =>
        {
            _logger.LogInformation("Message sent successfully after {Attempts} attempts", e.Attempts);
        };

        queueItem.Failed += (sender, e) =>
        {
            _logger.LogWarning("Message failed to send after {Attempts} attempts: {ErrorMessage}",
                e.Attempts, e.ErrorMessage);
        };
    }

    public int GetQueuedMessages() => _queue.QueuedMessageCount;
}
```

**Connection Manager Features:**
- **Automatic reconnection**: Tries to reconnect at regular intervals if connection is lost
- **Message queuing**: Messages are queued and sent when connection is available
- **Retry logic**: Failed messages are automatically retried up to a configurable limit
- **Event notifications**: Get notified when messages are sent successfully or fail
- **Background service**: Runs as a hosted service, no manual lifecycle management needed

### Manual Instantiation (without DI)

If you prefer not to use dependency injection:

```csharp
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SRF.Network.Udp;

var options = Options.Create(new UdpMulticastOptions
{
    MulticastAddress = "224.0.23.12",
    Port = 3671,
    TimeToLive = 16
});

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<UdpMulticastClient>();

using var client = new UdpMulticastClient(options, logger);

client.MessageReceived += (sender, e) =>
{
    Console.WriteLine($"Received {e.Data.Length} bytes");
};

await client.ConnectAsync();

// Send a message
byte[] data = new byte[] { 0x01, 0x02, 0x03 };
await client.SendAsync(data);

// Keep running...
await Task.Delay(TimeSpan.FromMinutes(5));

await client.DisconnectAsync();
```

## Configuration Options

### UdpMulticastOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MulticastAddress` | string | "224.0.0.1" | The multicast group IP address to join |
| `Port` | int | 5000 | The UDP port for multicast communication |
| `LocalInterface` | string? | null | Optional local interface IP to bind to. If null, binds to all interfaces |
| `TimeToLive` | int | 16 | TTL for multicast packets (1-255) |
| `ReceiveBufferSize` | int | 8192 | Size of the receive buffer in bytes |
| `SendBufferSize` | int | 8192 | Size of the send buffer in bytes |
| `ReuseAddress` | bool | true | Allow multiple sockets to bind to the same address/port |
| `ReceiveTimeout` | TimeSpan | 5 seconds | Timeout for receive operations |

### UdpConnectionManagerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ReconnectInterval` | double | 10.0 | Interval in seconds between connection attempts |
| `SendRetryInterval` | double | 5.0 | Interval in seconds to wait before retrying a failed send |
| `MaxSendAttempts` | int | 3 | Maximum number of send attempts before giving up on a message |
| `AutoConnect` | bool | true | Whether to automatically connect on startup |

## Common Use Cases

### KNX IP Routing

The library is ideal for KNX IP Routing which uses UDP multicast on 224.0.23.12:3671:

```json
{
  "Udp": {
    "Multicast": {
      "MulticastAddress": "224.0.23.12",
      "Port": 3671
    }
  }
}
```

### Custom Protocol Implementation

For implementing custom multicast protocols:

```csharp
public class MyProtocolHandler
{
    private readonly IUdpMulticastClient _client;
    private readonly IUdpMessageQueue _queue;

    // Inject both: _client for receiving, _queue for queued/reliable sending
    public MyProtocolHandler(IUdpMulticastClient client, IUdpMessageQueue queue)
    {
        _client = client;
        _queue = queue;
        _client.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, UdpMessageReceivedEventArgs e)
    {
        // Parse your protocol's binary format
        var message = MyProtocol.Parse(e.Data);

        // Handle the message
        HandleMessage(message);
    }

    /// <summary>
    /// Fire-and-forget: sends immediately if connected, throws if not.
    /// Suitable for real-time data where stale messages should be dropped.
    /// </summary>
    public async Task SendDirectAsync(MyProtocolMessage message)
    {
        byte[] data = MyProtocol.Encode(message);
        await _client.SendAsync(data);
    }

    /// <summary>
    /// Queued send: buffers the message until the connection is available,
    /// retries on failure. Suitable for commands that must not be lost.
    /// </summary>
    public void SendQueued(MyProtocolMessage message)
    {
        byte[] data = MyProtocol.Encode(message);
        var item = _queue.Enqueue(data);

        item.Failed += (_, e) =>
            Console.Error.WriteLine($"Send failed after {e.Attempts} attempts: {e.ErrorMessage}");
    }
}
```

## Logging

The library uses `ILogger<T>` for comprehensive logging:

- **Trace**: Detailed message send/receive operations
- **Debug**: Receive loop lifecycle events
- **Information**: Connection/disconnection events
- **Warning**: Socket exceptions, cleanup errors
- **Error**: Connection failures, send/receive failures

## Thread Safety

- The `IsConnected` property is thread-safe
- Event handlers are invoked on the receive loop thread
- Multiple threads can safely call `SendAsync` concurrently (protected by underlying socket)
- Connection state changes are synchronized

## IPv6 Support

Currently, the library supports IPv4 multicast. IPv6 support is planned for a future release.

## License

Part of the SRF.Network suite of libraries.
