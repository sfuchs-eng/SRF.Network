using System;
namespace SRF.Network.OpenHab.Client
{
    public class RestApiException : ApplicationException
    {
        public RestApiException() : base() { }
        public RestApiException(string? msg) : base(msg) { }
        public RestApiException(string? msg, Exception? inner) : base(msg, inner) { }
    }
}
