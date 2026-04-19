# SRF.Network.Knx

KNX client library for KNX/IP Routing communication. Supports Group Read, Write, and Response with automatic DPT decoding. Does not depend on the Falcon SDK — use `SRF.Network.Knx.Falcon` for Falcon-based connectivity.

## Quick Start

```csharp
// In Program.cs / host setup
builder.Services.AddSingleton<IKnxMasterDataProvider, KnxMasterDataProvider>(); // see below
builder.AddKnxIpRouting("Knx");

// Inject and use
public class MyService(IKnxConnection knx)
{
    public Task StartAsync(CancellationToken ct) => knx.ConnectAsync();

    // Receive decoded group messages
    void Subscribe() => knx.MessageReceived += (_, e) =>
    {
        var ctx = e.KnxMessageContext;
        Console.WriteLine($"{ctx.GroupEventArgs!.DestinationAddress} = {ctx.DecodedValue}");
    };

    // Send a group write
    Task Send(GroupAddress ga, GroupValue value, CancellationToken ct) =>
        knx.SendMessageAsync(GroupMessageRequest.Write(ga, value), ct);
}
```

## `AddKnxIpRouting(name)` — What Gets Registered

| Service | Type |
|---------|------|
| `IKnxConnection` | `KnxConnection` |
| `IKnxBus` | `KnxIpRoutingBus` (UDP transport) |
| `IDptResolver` | `KnxDptResolver` |
| `IDptFactory` | `DptMemoryCache(DptFactory)` |
| `IPdtEncoderFactory` | `PdtEncoderFactory` |
| `IDptNumericInfoFactory` | `DptNumericInfoFactory` |
| `IKnxConfigFactory` | `KnxConfigFactory` |
| `DomainConfiguration` | loaded from `KnxConfiguration.EtsGAExportFile` |
| `[FromKeyedServices(name)] IUdpMulticastClient` | `UdpMulticastClient` |
| `[FromKeyedServices(name)] IUdpMessageQueue` | `UdpMessageQueue` |
| `IHostedService` | `UdpConnectionManager` (reconnect + drain send queue) |

## Consumer Must Register

**`IKnxMasterDataProvider`** — required by `IDptFactory` to resolve DPT encoders from the KNX master data XML. `SRF.Knx.Core` ships `KnxMasterDataProvider` which loads the XML from `KnxConfiguration.KnxMasterFolder`:

```csharp
services.AddSingleton<IKnxMasterDataProvider, KnxMasterDataProvider>();
```

The KNX master data folder is configurable via `Knx:KnxMasterFolder` (defaults to `%AppData%/knx-master`). Download the KNX master data XML from the KNX Association and place it there.

## Configuration

```json
{
  "Knx": {
    "ConnectionString": "Type=IpRouting;KnxAddress=1.1.5",
    "EtsGAExportFile": "GroupAddressExport.xml",
    "KnxDomainConfigFile": "KnxDomainConfig.json",
    "KnxMasterFolder": "/etc/knx-master"
  },
  "Udp": {
    "Connections": {
      "Knx": {
        "MulticastAddress": "224.0.23.12",
        "Port": 3671,
        "ConnectionManager": {
          "ReconnectInterval": "00:00:10",
          "AutoConnect": true
        }
      }
    }
  }
}
```

| Key | Description |
|-----|-------------|
| `Knx:ConnectionString` | Falcon SDK syntax: `;`-separated `key=value`. `KnxAddress` sets the local individual address used in outbound cEMI frames (default: `0.0.1`). |
| `Knx:EtsGAExportFile` | Path to the ETS group address export XML file (required for DPT resolution). |
| `Udp:Connections:{name}` | UDP multicast address and port for KNX/IP Routing (standard: `224.0.23.12:3671`). |
