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


## Planned, time-line open

- `OpenHab`
C# .NET stack handling a subset of the item/channel related events on an [OpenHAB](https://www.openhab.org/) [event bus](https://www.openhab.org/docs/developer/utils/events.html#api-introduction) connected to via a WebSocket.  
Existing .NET Framework 4.8 code awaits porting to .NET 9+ into this solution here.

- `Knx`
KNX Net/IP Routing library allowing .NET applications to interact with KNX systems through reading/writing group address values.
    - not homologized, not standards compliant
    - limited to receiving Group Address read/write/read-answer packets
    - only routing connections using UDP broadcasts, no tunnelling connections to a Knx/IP router.
    - integrating with the KNX Group Address export XML file of ETS 5
