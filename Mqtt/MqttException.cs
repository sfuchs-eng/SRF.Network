using System;

namespace SRF.Network.Mqtt;

public class MqttException : ApplicationException
{
    public MqttException() : base() { }
    public MqttException(string? msg) : base(msg) { }
    public MqttException(string? msg, Exception? inner) : base(msg, inner) { }
}
