# SRF.Network.OpenHab

OpenHAB integration library for .NET (`net10.0`) with:

- WebSocket Event Bus client (`IEventBusClient`)
- Optional hosted reconnect wrapper (`OpenHabConnector`)
- REST API client (`IRestApiClient`)
- Event model and factory abstractions (`IEvent`, `IEventFactory`)

## Install / reference

Reference project:

```xml
<ProjectReference Include="..\OpenHab\SRF.Network.OpenHab.csproj" />
```

Or add the package when published.

## Configuration

`AddOpenHabConnector(...)` binds `EventBusClientOptions` from configuration section `OpenHAB` by default.

Example `appsettings.json`:

```json
{
  "OpenHAB": {
    "Disable": false,
    "WebSocket": "ws://localhost:8080/ws",
    "RestApi": "http://localhost:8080/rest/",
    "AccessToken": "<token>",
    "SourceEntity": "HomeCompanion",
    "FilterSource": true,
    "ClientBufferSize": 1024,
    "ReconnectWaitTime": 5000
  }
}
```

## Usage: with OpenHabConnector (hosted auto reconnect)

This is the easiest setup when running in a host (`IHost` / worker / server app).

```csharp
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

var builder = Host.CreateApplicationBuilder(args);

// Registers:
// - IEventFactory
// - IRestApiClient
// - IEventBusClient
// - OpenHabConnector as IHostedService
builder.Services.AddOpenHabConnector("OpenHAB");

using var host = builder.Build();

var eventBus = host.Services.GetRequiredService<IEventBusClient>();

eventBus.EventReceived += (sender, args) =>
{
    // Filter and inspect events
    if (args.IsItem("LivingRoomLight"))
    {
        Console.WriteLine($"[{args.When:O}] {args.Received.Topic}: {args.Received.PayloadJson}");
    }
};

// Queue an item command to transmit over the event bus
// (uses EventType.ItemCommandEvent under the hood)
eventBus.Command("LivingRoomLight", ItemStateSwitch.ON);

await host.RunAsync();
```

Notes:

- `OpenHabConnector` calls `IEventBusClient.ConnectAsync(...)` and reconnects on failures.
- `IEventBusClient` and `IEventFactory` are singletons by design.

## Usage: without OpenHabConnector (direct IEventBusClient)

Use this if you need full lifecycle control over connect/close behavior.

```csharp
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions();
builder.Services.AddOptions<EventBusClientOptions>()
    .BindConfiguration("OpenHAB");
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IEventFactory, EventFactory>();
builder.Services.AddSingleton<IRestApiClient, RestApiClient>();
builder.Services.AddSingleton<IEventBusClient, EventBusClient>();

using var host = builder.Build();
var eventBus = host.Services.GetRequiredService<IEventBusClient>();

using var cts = new CancellationTokenSource();

var connectTask = eventBus.ConnectAsync(cts.Token);

eventBus.EventReceived += (sender, args) =>
{
    Console.WriteLine($"RX {args.Received.Type}: {args.Received.Topic}");
};

// Send via helper command API
eventBus.Command("KitchenLight", ItemStateSwitch.OFF);

// Or create and enqueue custom events via the event factory
var ping = eventBus.EventFactory.CreatePing();
eventBus.EnqueueTransmit(ping);

// ... app logic ...

cts.Cancel();
await eventBus.CloseAsync(CancellationToken.None);
await connectTask;
```

## Usage: REST API (`IRestApiClient`)

`IRestApiClient` currently exposes item inventory retrieval.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOpenHabConnector("OpenHAB");

using var host = builder.Build();

var restApi = host.Services.GetRequiredService<IRestApiClient>();
var items = await restApi.GetItemsAsync(CancellationToken.None);

foreach (var item in items)
{
    Console.WriteLine($"{item.Name} ({item.Type}) = {item.State}");
}
```

## When to choose which setup

- Use `AddOpenHabConnector(...)` when you want automatic connect/reconnect managed by hosting.
- Use direct `IEventBusClient` when you need precise lifecycle orchestration and explicit connect/close calls.
- Use `IRestApiClient` for inventory/state reads from OpenHAB REST endpoints.

## Error behavior

- Event parsing is fail-fast for unknown event types: unsupported `type` values raise parsing/protocol exceptions.
- Connection-level failures are surfaced as connection/protocol exceptions and logged through `ILogger`.
