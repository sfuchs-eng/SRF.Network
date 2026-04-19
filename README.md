# SRF.Network

Collection of small convenience libraries in context of networking.
Originally built in context of a hobby grade, custom home automation solution.

## Presently included

- `Cli`  
Command line interface tool, primarily for manual integration testing

- `Misc`  
Various extensions, helpers, ...
  - e.g. the `HttpClientNoCertValidation`

- `Mqtt`  
A wrapper library bringing [MQTTnet](https://github.com/dotnet/MQTTnet) into a dependency injection / hosting context.

- `Test`  
(empty) unit test library for some of the libraries in this solution.

- `WebSocket`  
Client (Json)WebSocket related wrappers and helpers designed to receive streams of Json objects from a server.

## In work

- `Knx`  
  - A library to work with KNX, focused on receiving/transmitting KNX telegrams and handling the related configuration data. It depends on the `SRF.Knx.Core` and `SRF.Knx.Config` libraries which are also part of this solution.

## Planned, time-line open

- `OpenHab`  
C# .NET stack handling a subset of the item/channel related events on an [OpenHAB](https://www.openhab.org/) [event bus](https://www.openhab.org/docs/developer/utils/events.html#api-introduction) connected to via a WebSocket.  
Existing .NET Framework 4.8 code awaits porting to .NET 10 into this solution here.

## No plans to finish those

- `Knx.Falcon`
  - For now contains code evacuated from the `SRF.Knx` library as that one has been restructured to be independent of the Falcon SDK.
  - Wrapper for the official KNX library [Knx.Falcon.Sdk](https://www.nuget.org/packages/Knx.Falcon.Sdk)
  to work with `SRF.Knx`.
  - For more about the KNX Falcon SDK: [Falcon.SDK 6, Get started page on knx.org](https://support.knx.org/hc/en-us/sections/4410811049618-Get-Started)
  - Configuration handling from ETS to C# / .NET / Falcon.SDK is supported by `SRF.Knx.Config`.
