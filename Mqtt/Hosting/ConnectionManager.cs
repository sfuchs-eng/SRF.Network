using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Mqtt.Hosting;

public class ConnectionManager(IMqttBrokerConnection brokerConnection, ILogger<ConnectionManager> logger) : IHostedService
{
    public IMqttBrokerConnection BrokerConnection { get; } = brokerConnection;
    public ILogger<ConnectionManager> Logger { get; } = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await BrokerConnection.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await BrokerConnection.StopAsync(cancellationToken);
    }
}
