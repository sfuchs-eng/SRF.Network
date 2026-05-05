using System;

namespace SRF.Network.OpenHab.Client;

public class ProtocolException : ApplicationException
{
    public ProtocolException() : base() { }
    public ProtocolException(string? msg) : base(msg) { }
    public ProtocolException(string? msg, Exception? inner) : base(msg, inner) { }
}
