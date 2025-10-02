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

during app setup:

```
use SRF.Network.Mqtt.Hosting;

IHostBuilder hostBuilder = ...
hostBuilder.AddMqtt()
```

The configurable properties can be found in `SRF.Network.Mqtt.MqttOptions`.
A minimalistic config section in `appsettings.json` or an other suitable place may look as follows:
```
    "Mqtt": {
        "Host": "my.broker.host.org",
        "UseTls": false,
        "User": "...",
        "Pass": "..."
    }

```

then inject `IMqttBrokerConnection` to your services.

To publish messages, use
```
public class MyPublishingLogic(IMqttBrokerConnection brokerConnection)
{
    readonly IMqttBrokerConnection brokerConn = brokerConnection;

    public void SendIt<TObject>(TObject message) where TObject : class
    {
        var topic = "my/topic/for/message";
        var publishingQueueItem = brokerConn.PublishJson(topic, message);
        publishingQueueItem.Published += MessagePublishedHandler; // can be skipped
    }

    private void MessagePublishedHandler(object sender, PublishEventArgs args)
    {
        // ...
    }
}
```

The `Publish` and `PublishJson` methods of the `MqttBrokerConnection` implementation of `IMqttBrokerConnection` are
non-blocking. They queue the item for subsequent publishing by another Task / Thread.

Use an `IPublisher` implementation passed to the corresponding `IMqttBrokerConnection.Publish` method for fine-grained control of the publishing details.
The following `IPublisher` implementations are available in the library (as of 0.9.0): `PublisherString`, `PublisherJson`
