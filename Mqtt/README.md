# DI wrapper for MQTTnet Client

## Context

`SRF.Network.Mqtt` aims to provide simple use of [MQTTnet](https://github.com/dotnet/MQTTnet) with dependency injection.

It's minimalistic in flexibility and for trivial use cases only. It was developed as a service in a particular application before carving the code out as a dedicated library.

## Usage

```
use SRF.Network.Mqtt.Hosting;

IHostBuilder hostBuilder = ...
hostBuilder.AddMqtt()
```

then inject `IMqttBrokerConnection`.
