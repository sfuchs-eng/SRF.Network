using System;
namespace SRF.Network.OpenHab.Client
{
    public class ConnectionException : ApplicationException
    {
        public ConnectionException() : base() { }
        public ConnectionException(string msg) : base(msg) { }
        public ConnectionException(string msg, Exception inner) : base(msg, inner) { }
    }
}
