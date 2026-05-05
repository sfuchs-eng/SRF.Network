using System;
namespace SRF.Network.OpenHab.EventBus
{
    public class EventParsingException : ApplicationException
    {
        public EventParsingException() : base() { }
        public EventParsingException(string msg) : base(msg) { }
        public EventParsingException(string msg, Exception inner) : base(msg, inner) { }
    }
}
