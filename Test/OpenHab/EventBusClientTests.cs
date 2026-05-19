using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class EventBusClientTests
{
    private static EventBusClient CreateClient(bool enable = true)
    {
        var options = Options.Create(new EventBusClientOptions
        {
            Enable = enable,
            WebSocket = "ws://localhost:8080/ws",
            AccessToken = "token",
        });

        var factory = Substitute.For<IEventFactory>();
        return new EventBusClient(options, factory, NullLogger<EventBusClient>.Instance);
    }

    [Test]
    public void IsConnected_Initially_False()
    {
        var client = CreateClient();

        Assert.That(client.IsConnected, Is.False);
    }

    [Test]
    public void IsActive_Initially_False()
    {
        var client = CreateClient();

        Assert.That(client.IsActive, Is.False);
    }

    [Test]
    public void ConnectAsync_WhenDisabled_CompletesOnCancellation()
    {
        var client = CreateClient(enable: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await client.ConnectAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
