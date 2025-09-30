namespace SRF.Network.Mqtt;

public class MqttConnectionServiceException : MqttException
{
    public MqttConnectionServiceException() : base() { }
    public MqttConnectionServiceException(string msg) : base(msg) { }
    public MqttConnectionServiceException(string msg, Exception inner) : base(msg, inner) { }
}
