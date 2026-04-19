using SRF.Network.Knx.IpRouting;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="KnxIpRoutingPayloadProvider"/>.
/// </summary>
[TestFixture]
public class KnxIpRoutingPayloadProviderTests
{
    private KnxIpRoutingPayloadProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new KnxIpRoutingPayloadProvider();
    }

    [Test]
    public void AssignPayload_RoutingIndicationHeader_ReturnsTrue_AndAssignsCemiFrame()
    {
        var header = new KnxIpHeader { ServiceType = KnxIpHeader.RoutingIndicationServiceType };

        bool result = _provider.AssignPayload(header);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(header.Payload, Is.InstanceOf<CemiLDataFrame>());
        });
    }

    [Test]
    public void AssignPayload_WrongServiceType_ReturnsFalse_AndPayloadRemainsNull()
    {
        var header = new KnxIpHeader { ServiceType = 0x0201 }; // Search Request, not Routing

        bool result = _provider.AssignPayload(header);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(header.Payload, Is.Null);
        });
    }

    [Test]
    public void AssignPayload_NonKnxIpHeaderPacket_ReturnsFalse()
    {
        // Pass a CemiLDataFrame (not a KnxIpHeader)
        var frame = new CemiLDataFrame();

        bool result = _provider.AssignPayload(frame);

        Assert.That(result, Is.False);
    }

    [Test]
    public void AssignPayload_CalledTwice_ReplacesPayload()
    {
        var header = new KnxIpHeader { ServiceType = KnxIpHeader.RoutingIndicationServiceType };

        _provider.AssignPayload(header);
        var firstPayload = header.Payload;
        _provider.AssignPayload(header);

        // Each call creates a fresh CemiLDataFrame instance
        Assert.That(header.Payload, Is.Not.SameAs(firstPayload));
    }
}
