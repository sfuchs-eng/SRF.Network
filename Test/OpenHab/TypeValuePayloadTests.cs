using Microsoft.Extensions.Logging.Abstractions;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;
using UnitsNet;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class TypeValuePayloadTests
{
    [Test]
    public void Set_ItemStateSwitchOn_SetsOnOffTypeAndOnValue()
    {
        var payload = new TypeValuePayload().Set(ItemStateSwitch.ON);

        Assert.Multiple(() =>
        {
            Assert.That(payload.Type, Is.EqualTo("OnOff"));
            Assert.That(payload.Value, Is.EqualTo("ON"));
            Assert.That(payload.IsOn(), Is.True);
        });
    }

    [Test]
    public void Set_ItemStateContactOpen_SetsOpenClosedTypeAndOpenValue()
    {
        var payload = new TypeValuePayload().Set(ItemStateContact.OPEN);

        Assert.Multiple(() =>
        {
            Assert.That(payload.Type, Is.EqualTo("OpenClosed"));
            Assert.That(payload.Value, Is.EqualTo("OPEN"));
            Assert.That(payload.IsOpen(), Is.True);
        });
    }

    [Test]
    public void Set_Int_SetsDecimalType()
    {
        var payload = new TypeValuePayload().Set(42);

        Assert.Multiple(() =>
        {
            Assert.That(payload.Type, Is.EqualTo("Decimal"));
            Assert.That(payload.Value, Is.EqualTo("42"));
        });
    }

    [Test]
    public void Set_Quantity_SetsQuantityType()
    {
        var payload = new TypeValuePayload().Set(Temperature.FromDegreesCelsius(21.5));

        Assert.Multiple(() =>
        {
            Assert.That(payload.Type, Is.EqualTo("Quantity"));
            Assert.That(payload.Value, Does.Contain("21.5"));
            Assert.That(payload.Value, Does.Contain("°C"));
        });
    }

    [Test]
    public void Parse_ItemStateSwitch_RoundTrips()
    {
        var payload = new TypeValuePayload().Set(ItemStateSwitch.OFF);

        var parsed = payload.Parse<ItemStateSwitch>();

        Assert.That(parsed, Is.EqualTo(ItemStateSwitch.OFF));
    }

    [Test]
    public void Parse_Int_ThrowsArgumentException()
    {
        var payload = new TypeValuePayload { Type = "Decimal", Value = "42" };

        Assert.That(() => payload.Parse<int>(), Throws.TypeOf<ArgumentException>());
    }
}