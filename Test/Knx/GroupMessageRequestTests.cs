using SRF.Knx.Core;
using SRF.Network.Knx.Messages;

namespace SRF.Network.Test.Knx;

/// <summary>
/// Unit tests for <see cref="GroupMessageRequest"/>.
/// </summary>
[TestFixture]
public class GroupMessageRequestTests
{
    private static readonly GroupAddress TestAddress = new("1/2/3");
    private static readonly GroupValue TestValue = new([0x01]);

    // -------------------------------------------------------------------------
    // Constructor validation
    // -------------------------------------------------------------------------

    [Test]
    public void Constructor_NullDestinationAddress_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GroupMessageRequest(null!, TestValue, GroupEventType.ValueWrite));
    }

    [Test]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GroupMessageRequest(TestAddress, null!, GroupEventType.ValueWrite));
    }

    [Test]
    public void Constructor_SetsAllProperties()
    {
        var request = new GroupMessageRequest(TestAddress, TestValue, GroupEventType.ValueWrite, MessagePriority.Alarm);

        Assert.Multiple(() =>
        {
            Assert.That(request.DestinationAddress, Is.SameAs(TestAddress));
            Assert.That(request.Value, Is.SameAs(TestValue));
            Assert.That(request.EventType, Is.EqualTo(GroupEventType.ValueWrite));
            Assert.That(request.Priority, Is.EqualTo(MessagePriority.Alarm));
        });
    }

    [Test]
    public void Constructor_DefaultPriority_IsLow()
    {
        var request = new GroupMessageRequest(TestAddress, TestValue, GroupEventType.ValueWrite);
        Assert.That(request.Priority, Is.EqualTo(MessagePriority.Low));
    }

    // -------------------------------------------------------------------------
    // Write factory method
    // -------------------------------------------------------------------------

    [Test]
    public void Write_SetsEventTypeToValueWrite()
    {
        var msg = GroupMessageRequest.Write(TestAddress, TestValue);
        Assert.That(msg.EventType, Is.EqualTo(GroupEventType.ValueWrite));
    }

    [Test]
    public void Write_SetsCorrectAddressAndValue()
    {
        var msg = GroupMessageRequest.Write(TestAddress, TestValue);
        Assert.Multiple(() =>
        {
            Assert.That(msg.DestinationAddress, Is.SameAs(TestAddress));
            Assert.That(msg.Value, Is.SameAs(TestValue));
        });
    }

    [Test]
    public void Write_DefaultPriority_IsLow()
    {
        var msg = GroupMessageRequest.Write(TestAddress, TestValue);
        Assert.That(msg.Priority, Is.EqualTo(MessagePriority.Low));
    }

    [Test]
    public void Write_CustomPriority_IsRespected()
    {
        var msg = GroupMessageRequest.Write(TestAddress, TestValue, MessagePriority.Normal);
        Assert.That(msg.Priority, Is.EqualTo(MessagePriority.Normal));
    }

    // -------------------------------------------------------------------------
    // Read factory method
    // -------------------------------------------------------------------------

    [Test]
    public void Read_SetsEventTypeToValueRead()
    {
        var msg = GroupMessageRequest.Read(TestAddress);
        Assert.That(msg.EventType, Is.EqualTo(GroupEventType.ValueRead));
    }

    [Test]
    public void Read_ValueIsEmpty()
    {
        var msg = GroupMessageRequest.Read(TestAddress);
        Assert.That(msg.Value.Value, Is.Empty);
    }

    [Test]
    public void Read_SetsCorrectAddress()
    {
        var msg = GroupMessageRequest.Read(TestAddress);
        Assert.That(msg.DestinationAddress, Is.SameAs(TestAddress));
    }

    [Test]
    public void Read_DefaultPriority_IsLow()
    {
        var msg = GroupMessageRequest.Read(TestAddress);
        Assert.That(msg.Priority, Is.EqualTo(MessagePriority.Low));
    }

    // -------------------------------------------------------------------------
    // Response factory method
    // -------------------------------------------------------------------------

    [Test]
    public void Response_SetsEventTypeToValueResponse()
    {
        var msg = GroupMessageRequest.Response(TestAddress, TestValue);
        Assert.That(msg.EventType, Is.EqualTo(GroupEventType.ValueResponse));
    }

    [Test]
    public void Response_SetsCorrectAddressAndValue()
    {
        var msg = GroupMessageRequest.Response(TestAddress, TestValue);
        Assert.Multiple(() =>
        {
            Assert.That(msg.DestinationAddress, Is.SameAs(TestAddress));
            Assert.That(msg.Value, Is.SameAs(TestValue));
        });
    }

    [Test]
    public void Response_CustomPriority_IsRespected()
    {
        var msg = GroupMessageRequest.Response(TestAddress, TestValue, MessagePriority.System);
        Assert.That(msg.Priority, Is.EqualTo(MessagePriority.System));
    }

    // -------------------------------------------------------------------------
    // MessagePriority encoding (used for Ctrl1 bit encoding in KnxIpRoutingBus)
    // -------------------------------------------------------------------------

    [Test]
    public void MessagePriority_System_HasValue0()  => Assert.That((int)MessagePriority.System, Is.EqualTo(0));
    [Test]
    public void MessagePriority_Normal_HasValue1()  => Assert.That((int)MessagePriority.Normal, Is.EqualTo(1));
    [Test]
    public void MessagePriority_Alarm_HasValue2()   => Assert.That((int)MessagePriority.Alarm, Is.EqualTo(2));
    [Test]
    public void MessagePriority_Low_HasValue3()     => Assert.That((int)MessagePriority.Low, Is.EqualTo(3));
}
