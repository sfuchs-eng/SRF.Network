using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Network.Udp;

namespace SRF.Network.Test.Udp;

[TestFixture]
public class UdpMulticastClientTests
{
    private static UdpMulticastClient CreateClient(UdpMulticastOptions options, ILogger<UdpMulticastClient>? logger = null)
    {
        return new UdpMulticastClient(
            Options.Create(options),
            logger ?? Substitute.For<ILogger<UdpMulticastClient>>(),
            TimeProvider.System);
    }

    [Test]
    public void IsConnected_Initially_False()
    {
        var client = CreateClient(new UdpMulticastOptions());

        Assert.That(client.IsConnected, Is.False);
    }

    [Test]
    public void ConnectAsync_InvalidMulticastAddress_ThrowsArgumentException()
    {
        var client = CreateClient(new UdpMulticastOptions
        {
            MulticastAddress = "not-an-address",
            Port = 5000,
        });

        Assert.That(async () => await client.ConnectAsync(CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<UdpMulticastClient>>();
        var client = CreateClient(new UdpMulticastOptions(), logger);

        Assert.DoesNotThrowAsync(async () => await client.DisconnectAsync(CancellationToken.None));
    }

    [Test]
    public void SendAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        var client = CreateClient(new UdpMulticastOptions());

        Assert.That(async () => await client.SendAsync([0x01], CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }
}
