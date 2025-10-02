# DI wrapper for MQTTnet Client

## Context

`SRF.Network.Mqtt` aims to provide simple use of [MQTTnet](https://github.com/dotnet/MQTTnet) with dependency injection.

It's minimalistic in flexibility and for trivial use cases only. It was developed as a service in a particular application before carving the code out as a dedicated library.

Present code allows only 1 broker to be connected to. In order to connect to multiple brokers, the library would need to be expanded e.g. relying on keyed singleton services and a logic to select the configuration section according the service registration key.

## Package
```
dotnet add package SRF.Network.Mqtt
```


## Usage

```
use SRF.Network.Mqtt.Hosting;

IHostBuilder hostBuilder = ...
hostBuilder.AddMqtt()
```

then inject `IMqttBrokerConnection` to your services.
