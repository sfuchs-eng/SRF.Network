# SRF.Network.WebSocket

Utility library for receiving JSON object streams over WebSocket in a .NET hosting context.
It is built on top of the standard `System.Net.WebSockets` namespace and provides a higher-level API
for connecting to a WebSocket endpoint, deserializing incoming messages, and automatically reconnecting
on failures — with full Dependency Injection and `BackgroundService` support.

## Features

- Strongly-typed JSON receive loop with automatic deserialization via `System.Text.Json`
- Send arbitrary objects as JSON over an established WebSocket connection
- Automatic reconnect on connection failures with configurable delay
- TLS certificate validation bypass for development / internal endpoints (`InsecureWebSocket`)
- Customizable HTTP request headers (e.g. `Authorization`) on the WebSocket upgrade request
- Configurable receive/send buffer backed by `ArrayPool<byte>` to minimize allocations
- Reader/writer `SemaphoreSlim` locks enforcing the WebSocket single-reader/single-writer constraint
- Event-driven message reception via `EventHandler<MessageReceivedEventArgs<T>>`
- `WebSocketClosed` event when the server initiates a close handshake
- `BackgroundService` integration (`JsonReceiver<T>`) for use with the .NET Generic Host

## Installation

Add the project reference to your project:

```bash
dotnet add reference path/to/SRF.Network.WebSocket.csproj
```

## Usage

### Hosted JSON Receiver with Dependency Injection

`JsonReceiver<T>` is a `BackgroundService` that connects to a WebSocket endpoint, continuously
deserializes incoming messages as `T`, and raises `MessageReceived` events. It reconnects
automatically when the connection drops.

#### 1. Define the message type

```csharp
public class SensorReading
{
    public string SensorId { get; set; } = "";
    public double Value    { get; set; }
}
```

#### 2. Configure in `appsettings.json`

```json
{
  "Sensors": {
    "Url": "ws://sensor-hub.local:8080/stream",
    "Insecure": true,
    "ReceiveBufferSize": 10240,
    "ReconnectDelaySec": 30
  }
}
```

#### 3. Register the service

```csharp
using SRF.Network.WebSocket;

services.Configure<JsonReceiverConfig<SensorReading>>(
    builder.Configuration.GetSection("Sensors"));

services.AddHostedService<JsonReceiver<SensorReading>>();
```

#### 4. Subscribe to messages

Inject `JsonReceiver<SensorReading>` and subscribe to `MessageReceived` in another hosted service
or consumer:

```csharp
public class SensorConsumer : IHostedService
{
    private readonly JsonReceiver<SensorReading> _receiver;

    public SensorConsumer(JsonReceiver<SensorReading> receiver)
    {
        _receiver = receiver;
        _receiver.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs<SensorReading> e)
    {
        Console.WriteLine($"Sensor {e.Message.SensorId}: {e.Message.Value}");
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct)  => Task.CompletedTask;
}
```

Register `JsonReceiver<SensorReading>` as both its concrete type (so it can be injected) and as a
hosted service, giving consumers the same instance:

```csharp
services.AddSingleton<JsonReceiver<SensorReading>>();
services.AddHostedService(sp => sp.GetRequiredService<JsonReceiver<SensorReading>>());
```

### Manual Instantiation (without DI)

For command-line tools or simple scripts, the stack can be composed directly without a host:

```csharp
using Microsoft.Extensions.Logging;
using SRF.Network.Misc;
using SRF.Network.WebSocket;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

var ws = new JsonWebSocket(
    new InsecureWebSocket(
        new HttpClientNoCertValidation(),
        loggerFactory.CreateLogger<InsecureWebSocket>()),
    loggerFactory.CreateLogger<JsonWebSocket>());

ws.WebSocketClosed += (_, _) => Console.WriteLine("Server closed the connection.");

await ws.ConnectAsync(new Uri("ws://localhost:8080"), CancellationToken.None);

// Receive a strongly-typed message
var reading = await ws.ReceiveAsync<SensorReading>(CancellationToken.None);
Console.WriteLine($"Got: {reading?.Value}");

// Or receive raw text
var raw = await ws.ReceiveStringAsync(CancellationToken.None);
Console.WriteLine(raw);

// Send a message
await ws.SendAsync(new SensorReading { SensorId = "T1", Value = 21.5 }, CancellationToken.None);

await ws.DisconnectAsync("done", CancellationToken.None);
```

### Adding Custom Request Headers

`InsecureWebSocket` exposes a `ConfigureHeaders` property that is called during the WebSocket
upgrade request. Use it to inject authentication or other custom headers:

```csharp
var transport = new InsecureWebSocket(new HttpClientNoCertValidation(), logger);
transport.ConfigureHeaders = headers =>
{
    headers.Add("Authorization", "Bearer <token>");
};
```

### Customizing JSON Options

Subclass `JsonWebSocket` and override `InitializeJsonOptions` to add custom converters or change
serialization behaviour:

```csharp
public class MyWebSocket(IWebSocketWrapper transport, ILogger logger)
    : JsonWebSocket(transport, logger)
{
    protected override void InitializeJsonOptions()
    {
        base.InitializeJsonOptions();
        JsonOptionsSend.Converters.Add(new MyCustomConverter());
    }
}
```

## Configuration Reference (`JsonReceiverConfig<T>`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Url` | `string` | `ws://localhost:8080` | WebSocket endpoint URL |
| `Insecure` | `bool` | `true` | Skip TLS certificate validation (only `true` supported currently) |
| `ReceiveBufferSize` | `int` | `10240` | Receive/send buffer size in bytes |
| `ReconnectDelaySec` | `int` | `60` | Seconds to wait before reconnecting after a failure |

## Key Types

| Type | Description |
|---|---|
| `IWebSocketWrapper` | Core transport abstraction: connect, disconnect, `IsConnected`, reader/writer locks |
| `InsecureWebSocket` | `IWebSocketWrapper` implementation that skips TLS certificate validation |
| `JsonWebSocket` | Decorator over `IWebSocketWrapper` adding JSON serialization/deserialization |
| `JsonReceiver<T>` | `BackgroundService` with auto-reconnect loop; raises `MessageReceived` events |
| `JsonReceiverConfig<T>` | Options class for configuring `JsonReceiver<T>` |
| `MessageReceivedEventArgs<T>` | Event args carrying the deserialized message |

## Limitations

As my use case of just receiving JSON messages from internal endpoints is fairly specific, there are some limitations to be aware of:

- **TLS with certificate validation** is not yet implemented. Only `InsecureWebSocket` is available; use it for development or internal endpoints. For production scenarios, implement a custom
  `IWebSocketWrapper` that performs proper TLS validation.
- **Send support in `JsonReceiver<T>`** — `JsonReceiver` is receive-only. For bidirectional
  communication, use `JsonWebSocket` directly.
- A single `JsonReceiver<T>` instance handles exactly one endpoint. To connect to multiple
  endpoints, register multiple `JsonReceiver<T>` instances with different configurations.
- Polymorphic deserialization (e.g. with `JsonConverter`) is not built-in but can be added by subclassing `JsonWebSocket` and customizing the `JsonSerializerOptions`.
