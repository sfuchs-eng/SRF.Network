# SRF.Network.Udp

Utility library simplifying the use of UDP Multicast messaging and related configuration.
It's built on top of the .NET `System.Net.Sockets` namespace and provides a higher-level API for common UDP operations.

It's initially designed for use with the SRF.Network.Knx library, but should serve as a general-purpose utility for any UDP-based communication needs.

## Features

- Configure what interfaces to bind to and send from
- Configure multicast group membership and TTL
- Send and receive messages with a simple API
- Support for both IPv4 and IPv6
- Automatic handling of socket options for multicast communication
- Asynchronous send and receive operations
- Logging of socket operations and errors
- Support for cancellation and timeouts
- Extensible design for future enhancements

## Usage
