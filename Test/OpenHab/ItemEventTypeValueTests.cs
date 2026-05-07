using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class ItemEventTypeValueTests
{
    [Test]
    public void Configure_ItemCommandEvent_SetsCommandTopicSegment()
    {
        var itemEvent = new ItemEventTypeValue();
        itemEvent.Configure(EventType.ItemCommandEvent);
        itemEvent.ForItem("KitchenLight");

        Assert.That(itemEvent.Topic, Is.EqualTo("openhab/items/KitchenLight/command"));
    }

    [Test]
    public void Configure_ItemStateEvent_SetsStateTopicSegment()
    {
        var itemEvent = new ItemEventTypeValue();
        itemEvent.Configure(EventType.ItemStateEvent);
        itemEvent.ForItem("KitchenLight");

        Assert.That(itemEvent.Topic, Is.EqualTo("openhab/items/KitchenLight/state"));
    }

    [Test]
    public void Set_Int_GetInt_RoundTrips()
    {
        var itemEvent = new ItemEventTypeValue().Set(42);

        Assert.That(itemEvent.GetInt(), Is.EqualTo(42));
    }

    [Test]
    public void Set_Double_GetDouble_RoundTrips()
    {
        var itemEvent = new ItemEventTypeValue().Set(21.5);

        Assert.That(itemEvent.GetDouble(), Is.EqualTo(21.5).Within(0.0001));
    }

    [Test]
    public void OnOffIsOn_WhenOn_ReturnsTrue()
    {
        var itemEvent = new ItemEventTypeValue().Set(ItemStateSwitch.ON);

        Assert.That(itemEvent.OnOffIsOn(), Is.True);
    }

    [Test]
    public void OnOffIsOn_WhenStateIsNotSwitch_ThrowsArgumentException()
    {
        var itemEvent = new ItemEventTypeValue().Set(5);

        Assert.That(() => itemEvent.OnOffIsOn(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OpenClosedIsOpen_WhenOpen_ReturnsTrue()
    {
        var itemEvent = new ItemEventTypeValue().Set(ItemStateContact.OPEN);

        Assert.That(itemEvent.OpenClosedIsOpen(), Is.True);
    }
}