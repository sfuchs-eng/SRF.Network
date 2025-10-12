# SRF.Network

Collection of small convenience libraries in context of networking.
Originally built for a custom home automation solution, use is expanding meanwhile.

## Presently included

- `Cli`  
Command line interface tool, primarily for manual integration testing

- `Misc`  
Various extensions, helpers, ...
  - e.g. the `HttpClientNoCertValidation`

- `Mqtt`  
A wrapper library bringing [MQTTnet](https://github.com/dotnet/MQTTnet) into a dependency injection / hosting context.

- `Test`  
(empty) unit test library. Only used for debugging issues observed in real-life applications.

- `WebSocket`  
Client (Json)WebSocket related wrappers and helpers designed to receive streams of Json objects from a server.

## In work

- `Knx`  
In development: Wrapper for the official KNX library [Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk) and KNX project/installation oriented additional functionality serving the easy implementation of custom C# .NET logic in home automation scenarios.  
For more about the KNX Falcon SDK: [Falcon.SDK 6, Get started page on knx.org](https://support.knx.org/hc/en-us/sections/4410811049618-Get-Started)

## Planned, time-line open

- `OpenHab`  
C# .NET stack handling a subset of the item/channel related events on an [OpenHAB](https://www.openhab.org/) [event bus](https://www.openhab.org/docs/developer/utils/events.html#api-introduction) connected to via a WebSocket.  
Existing .NET Framework 4.8 code awaits porting to .NET 9+ into this solution here.
