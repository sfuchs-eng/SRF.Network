using System;
namespace SRF.Network.OpenHab.EventBus
{
    public class EventException : ApplicationException
    {
        public EventException() : base() { }
        public EventException(string? msg) : base(msg) { }
        public EventException(string? msg, Exception? inner) : base(msg, inner) { }
    }
}
