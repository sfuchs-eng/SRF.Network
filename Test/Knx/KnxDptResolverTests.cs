using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SRF.Knx.Config.Domain;
using SRF.Knx.Config.ETS5;
using SRF.Knx.Core;
using SRF.Knx.Core.DPT;
using SRF.Network.Knx.Dpt;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="KnxDptResolver"/>.
/// </summary>
[TestFixture]
public class KnxDptResolverTests
{
    private IDptFactory _dptFactory = null!;
    private DomainConfiguration _domainConfig = null!;
    private KnxDptResolver _resolver = null!;

    // A stand-in DptBase implementation for testing (DptBase is abstract)
    private sealed class StubDpt : DptBase
    {
        public StubDpt(string id) => Id = new DataPointTypeId(id);
        public override object ToValue(GroupValue groupValue) => 0;
        public override GroupValue ToGroupValue(object value) => new([]);
    }

    [SetUp]
    public void SetUp()
    {
        _dptFactory = Substitute.For<IDptFactory>();
        _domainConfig = new DomainConfiguration();
        _resolver = new KnxDptResolver(
            _domainConfig,
            _dptFactory,
            NullLogger<KnxDptResolver>.Instance);
    }

    // -------------------------------------------------------------------------
    // Constructor validation
    // -------------------------------------------------------------------------

    [Test]
    public void Constructor_NullDomainConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KnxDptResolver(null!, _dptFactory, NullLogger<KnxDptResolver>.Instance));
    }

    [Test]
    public void Constructor_NullDptFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KnxDptResolver(_domainConfig, null!, NullLogger<KnxDptResolver>.Instance));
    }

    [Test]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KnxDptResolver(_domainConfig, _dptFactory, null!));
    }

    // -------------------------------------------------------------------------
    // GetDpt — null guard
    // -------------------------------------------------------------------------

    [Test]
    public void GetDpt_NullGroupAddress_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _resolver.GetDpt(null!));
    }

    // -------------------------------------------------------------------------
    // GetDpt — unknown address
    // -------------------------------------------------------------------------

    [Test]
    public void GetDpt_UnknownGroupAddress_ThrowsKnxException()
    {
        var address = new GroupAddress("0/0/99");
        // _domainConfig has no entries → should throw

        Assert.Throws<KnxException>(() => _resolver.GetDpt(address));
    }

    // -------------------------------------------------------------------------
    // GetDpt — address without valid DPT
    // -------------------------------------------------------------------------

    [Test]
    public void GetDpt_AddressWithNoDpt_ThrowsKnxException()
    {
        var address = new GroupAddress("0/0/1");
        // EtsGroupAddressConfig with no DPT set → HasValidDPT = false
        _domainConfig.GroupAddresses[address.Address] = new EtsGroupAddressConfig
        {
            Label = "Unlabeled GA",
            // DPT is left at default (invalid)
        };

        Assert.Throws<KnxException>(() => _resolver.GetDpt(address));
    }

    // -------------------------------------------------------------------------
    // GetDpt — happy path
    // -------------------------------------------------------------------------

    [Test]
    public void GetDpt_KnownAddressWithValidDpt_ReturnsDptFromFactory()
    {
        var address = new GroupAddress("0/0/1");
        var etsConfig = new EtsGroupAddressConfig { Label = "Light switch" };
        etsConfig.DPTs = "DPST-1-1"; // DPT 1.001 = boolean
        _domainConfig.GroupAddresses[address.Address] = etsConfig;

        var expectedDpt = new StubDpt("DPST-1-1") { Id = new DataPointTypeId(1, 1) };
        _dptFactory.Get(1, 1).Returns(expectedDpt);

        var result = _resolver.GetDpt(address);

        Assert.That(result, Is.SameAs(expectedDpt));
    }

    [Test]
    public void GetDpt_CallsDptFactoryWithCorrectMainAndSub()
    {
        var address = new GroupAddress("0/0/2");
        var etsConfig = new EtsGroupAddressConfig { Label = "Temperature" };
        etsConfig.DPTs = "DPST-9-1"; // DPT 9.001 = temperature °C
        _domainConfig.GroupAddresses[address.Address] = etsConfig;

        var dpt = new StubDpt("DPST-9-1") { Id = new DataPointTypeId(9, 1) };
        _dptFactory.Get(9, 1).Returns(dpt);

        _resolver.GetDpt(address);

        _dptFactory.Received(1).Get(9, 1);
    }

    // -------------------------------------------------------------------------
    // GetDpt — caching
    // -------------------------------------------------------------------------

    [Test]
    public void GetDpt_CalledTwiceForSameAddress_ReturnsSameInstance()
    {
        var address = new GroupAddress("0/0/1");
        var etsConfig = new EtsGroupAddressConfig { Label = "Dimmer" };
        etsConfig.DPTs = "DPST-5-1";
        _domainConfig.GroupAddresses[address.Address] = etsConfig;

        var dpt = new StubDpt("DPST-5-1") { Id = new DataPointTypeId(5, 1) };
        _dptFactory.Get(5, 1).Returns(dpt);

        var first = _resolver.GetDpt(address);
        var second = _resolver.GetDpt(address);

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetDpt_CachesResult_FactoryCalledOnlyOnce()
    {
        var address = new GroupAddress("0/0/1");
        var etsConfig = new EtsGroupAddressConfig { Label = "Blind" };
        etsConfig.DPTs = "DPST-5-1";
        _domainConfig.GroupAddresses[address.Address] = etsConfig;

        var dpt = new StubDpt("DPST-5-1") { Id = new DataPointTypeId(5, 1) };
        _dptFactory.Get(5, 1).Returns(dpt);

        _resolver.GetDpt(address);
        _resolver.GetDpt(address);
        _resolver.GetDpt(address);

        _dptFactory.Received(1).Get(5, 1);
    }

    [Test]
    public void GetDpt_DifferentAddresses_CachedIndependently()
    {
        var addr1 = new GroupAddress("0/0/1");
        var addr2 = new GroupAddress("0/0/2");

        var etc1 = new EtsGroupAddressConfig { Label = "GA1" };
        etc1.DPTs = "DPST-1-1";
        var etc2 = new EtsGroupAddressConfig { Label = "GA2" };
        etc2.DPTs = "DPST-9-1";

        _domainConfig.GroupAddresses[addr1.Address] = etc1;
        _domainConfig.GroupAddresses[addr2.Address] = etc2;

        var dpt1 = new StubDpt("DPST-1-1") { Id = new DataPointTypeId(1, 1) };
        var dpt2 = new StubDpt("DPST-9-1") { Id = new DataPointTypeId(9, 1) };
        _dptFactory.Get(1, 1).Returns(dpt1);
        _dptFactory.Get(9, 1).Returns(dpt2);

        var result1 = _resolver.GetDpt(addr1);
        var result2 = _resolver.GetDpt(addr2);

        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.SameAs(dpt1));
            Assert.That(result2, Is.SameAs(dpt2));
        });
    }
}
