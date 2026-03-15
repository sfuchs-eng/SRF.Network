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
- Dependency Injection (DI) support with keyed services (multiple simultaneous connections)
- Event-driven message reception
- Hosting extensions for easy integration with .NET Generic Host

## Installation

Add the project reference to your project:

```bash
dotnet add reference path/to/SRF.Network.Udp.csproj
```

## Usage

### Multiple Named Connections with Dependency Injection

Each UDP connection is identified by a **name** string. Services are registered as keyed singletons
and injected using the `[FromKeyedServices("name")]` attribute.

#### 1. Configure in appsettings.json

Named connections live under `Udp:Connections:{name}`. Connection-manager options are nested inside
each connection's block under `ConnectionManager`.

```json
{
  "Udp": {
    "Connections": {
      "Knx": {
        "MulticastAddress": "224.0.23.12",
        "Port": 3671,
        "TimeToLive": 16,
        "ConnectionManager": {
          "AutoConnect": true,
          "MaxSendAttempts": 5
        }
      },
      "Discovery": {
        "MulticastAddress": "239.0.0.1",
        "Port": 5001,
        "ConnectionManager": {
          "ReconnectInterval": 30.0
        }
      }
    }
  }
}
```

#### 2. Register Services

Using `IServiceCollection`:

```csharp
using SRF.Network.Udp.Hosting;

// Register two independent connections.
// Each name drives its own config section: Udp:Connections:{name}
services.AddUdpMulticastWithConnectionManager("Knx");
services.AddUdpMulticastWithConnectionManager("Discovery");

// Client-only (no connection manager / message queue):
services.AddUdpMulticast("Listener");
```

Using `IHostBuilder`:

```csharp
Host.CreateDefaultBuilder(args)
    .AddUdpMulticastWithConnectionManager("Knx")
    .AddUdpMulticastWithConnectionManager("Discovery")
    .ConfigureServices((context, services) =>
    {
        // Other services...
    })
    .Build();
```

Using `IHostApplicationBuilder`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddUdpMulticastWithConnectionManager("Knx");
builder.AddUdpMulticastWithConnectionManager("Discovery");
```

You can also override the configuration section for a named connection:

```csharp
// Reads multicast options from "MyApp:KnxUdp" instead of "Udp:Connections:Knx"
services.AddUdpMulticastWithConnectionManager("Knx", configSection: "MyApp:KnxUdp");
```

#### 3. Inject and Use

Use the `[FromKeyedServices("name")]` attribute to resolve the correct instance:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SRF.Network.Udp;

public class KnxProtocolHandler
{
    private readonly IUdpMulticastClient _client;
    private readonly IUdpMessageQueue    _queue;
    private readonly ILogger<KnxProtocolHandler> _logger;

    public KnxProtocolHandler(
        [FromKeyedServices("Knx")] IUdpMulticastClient client,
        [FromKeyedServices("Knx")] IUdpMessageQueue    queue,
        ILogger<KnxProtocolHandler> logger)
    {
        _client = client;
        _queue  = queue;
        _logger = logger;

        _client.MessageReceived         += OnMessageReceived;
        _client.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    public void SendQueued(byte[] data) => _queue.Enqueue(data);

    private void OnMessageReceived(object? sender, UdpMessageReceivedEventArgs e)
    {
        _logger.LogInformation("Received {ByteCount} bytes from {RemoteEndPoint}",
            e.Data.Length, e.RemoteEndPoint);
    }

    private void OnConnectionStatusChanged(object? sender, UdpConnectionEventArgs e)
    {
        if (e.IsConnected)
            _logger.LogInformation("KNX UDP connected");
        else
            _logger.LogWarning("KNX UDP disconnected: {ErrorMessage}", e.ErrorMessage);
    }
}
```

### Message Queue and Send Status Events

```csharp
public void SendWithFeedback(byte[] data)
{
    var item = _queue.Enqueue(data);

    item.Sent += (_, e) =>
        _logger.LogInformation("Sent after {Attempts} attempt(s)", e.Attempts);

    item.Failed += (_, e) =>
        _logger.LogWarning("Failed after {Attempts} attempt(s): {Error}", e.Attempts, e.ErrorMessage);
}
```

### Client-Only (no queue)

When registered with `AddUdpMulticast`, only an `IUdpMulticastClient` keyed singleton is created.
You are responsible for calling `ConnectAsync` / `DisconnectAsync` and sending directly.

```csharp
public class ListenerService : IHostedService
{
    private readonly IUdpMulticastClient _client;

    public ListenerService([FromKeyedServices("Listener")] IUdpMulticastClient client)
    {
        _client = client;
        _client.MessageReceived += (_, e) => Console.WriteLine($"Got {e.Data.Length} bytes");
    }

    public Task StartAsync(CancellationToken ct) => _client.ConnectAsync(ct);
    public Task StopAsync(CancellationToken ct)  => _client.DisconnectAsync(ct);
}
```

### Manual Instantiation (without DI)

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

client.MessageReceived += (_, e) => Console.WriteLine($"Received {e.Data.Length} bytes");

await client.ConnectAsync();

await client.SendAsync(new byte[] { 0x01, 0x02, 0x03 });

await Task.Delay(TimeSpan.FromMinutes(5));

await client.DisconnectAsync();
```

## Configuration Options

### UdpMulticastOptions

Configuration section per connection: `Udp:Connections:{name}`

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

Configuration section per connection: `Udp:Connections:{name}:ConnectionManager`

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
    "Connections": {
      "Knx": {
        "MulticastAddress": "224.0.23.12",
        "Port": 3671
      }
    }
  }
}
```

```csharp
services.AddUdpMulticastWithConnectionManager("Knx");
```

### Custom Protocol Implementation

```csharp
public class MyProtocolHandler
{
    private readonly IUdpMulticastClient _client;
    private readonly IUdpMessageQueue _queue;

    public MyProtocolHandler(
        [FromKeyedServices("MyProto")] IUdpMulticastClient client,
        [FromKeyedServices("MyProto")] IUdpMessageQueue    queue)
    {
        _client = client;
        _queue  = queue;
        _client.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, UdpMessageReceivedEventArgs e)
    {
        var message = MyProtocol.Parse(e.Data);
        HandleMessage(message);
    }

    /// <summary>
    /// Fire-and-forget: sends immediately if connected, throws if not.
    /// Suitable for real-time data where stale messages should be dropped.
    /// </summary>
    public async Task SendDirectAsync(MyProtocolMessage message)
        => await _client.SendAsync(MyProtocol.Encode(message));

    /// <summary>
    /// Queued send: buffers the message until the connection is available,
    /// retries on failure. Suitable for commands that must not be lost.
    /// </summary>
    public void SendQueued(MyProtocolMessage message)
    {
        var item = _queue.Enqueue(MyProtocol.Encode(message));
        item.Failed += (_, e) =>
            Console.Error.WriteLine($"Send failed after {e.Attempts} attempts: {e.ErrorMessage}");
    }
}
```

## Logging

The library uses `ILogger<T>` for comprehensive logging. Log messages from
`UdpConnectionManager` include a `{ConnectionName}` structured property, making it easy to
distinguish between connections in multi-connection scenarios.

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
