using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SRF.Network.Knx;
using SRF.Network.Udp;

namespace SRF.Network.Test.Knx;

[TestFixture]
public class KnxOptionsProjectionTests
{
    [Test]
    public void AddKnxIpRouting_ProjectsKnxConnectionOptions_ToNamedUdpOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Knx:Connections:default:MulticastAddress", "239.1.2.3"),
                new("Knx:Connections:default:Port", "4711"),
                new("Knx:Connections:default:LocalIpAddress", "192.168.55.10"),
                new("Knx:Connections:default:ReconnectInterval", "12.5"),
                new("Knx:Connections:default:SendRetryInterval", "3.5"),
                new("Knx:Connections:default:MaxSendAttempts", "7"),
                new("Knx:Connections:default:AutoConnect", "false"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddKnxIpRouting("default");

        using var sp = services.BuildServiceProvider();

        var udp = sp.GetRequiredService<IOptionsMonitor<UdpMulticastOptions>>().Get("default");
        var manager = sp.GetRequiredService<IOptionsMonitor<UdpConnectionManagerOptions>>().Get("default");

        Assert.Multiple(() =>
        {
            Assert.That(udp.MulticastAddress, Is.EqualTo("239.1.2.3"));
            Assert.That(udp.Port, Is.EqualTo(4711));
            Assert.That(udp.LocalIpAddress, Is.EqualTo("192.168.55.10"));
            Assert.That(manager.ReconnectInterval, Is.EqualTo(12.5));
            Assert.That(manager.SendRetryInterval, Is.EqualTo(3.5));
            Assert.That(manager.MaxSendAttempts, Is.EqualTo(7));
            Assert.That(manager.AutoConnect, Is.False);
        });
    }

    [Test]
    public void AddKnxIpRouting_StructuredKnxOptions_WinOverConnectionStringFallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Knx:Connections:default:ConnectionString", "Type=IpRouting;LocalIpAddress=10.1.1.1;Port=3672"),
                new("Knx:Connections:default:LocalIpAddress", "192.168.77.20"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddKnxIpRouting("default");

        using var sp = services.BuildServiceProvider();

        var udp = sp.GetRequiredService<IOptionsMonitor<UdpMulticastOptions>>().Get("default");

        Assert.Multiple(() =>
        {
            Assert.That(udp.LocalIpAddress, Is.EqualTo("192.168.77.20"));
            Assert.That(udp.Port, Is.EqualTo(3672));
        });
    }
}