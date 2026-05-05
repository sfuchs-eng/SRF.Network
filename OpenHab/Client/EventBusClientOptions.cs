using System;
namespace SRF.Network.OpenHab.Client
{
    public class EventBusClientOptions
    {
        /// <summary>
        /// For offline testing purpose. Prevents connecting to an OpenHAB instance but
        /// still instanciates related services.
        /// </summary>
        public bool Disable { get; set; } = false;

        /// <summary>
        /// Complete OpenHAB server websocket URI used with <see cref="System.Net.WebSockets.ClientWebSocket"/>.
        /// Use "wss://localhost:8443/ws" for SSL and "ws://localhost:8080/ws" for a plain connection to default ports.
        /// </summary>
        public string WebSocket { get; set; } = "ws://localhost:8080/ws";

        /// <summary>
        /// OpenHAB server rest api base URI, with trailing slash.
        /// </summary>
        public Uri RestApi { get; set; } = new Uri("http://localhost:8080/rest/");

        /// <summary>
        /// The OpenHAB access token.
        /// It's passed as query parameter appended to <see cref="WebSocket"/>, "?accessToken=...".
        /// And is used as Authentication Bearer header to <see cref="RestApi"/> requests by <see cref="RestApiClient"/>.
        /// </summary>
        public string AccessToken { get; set; } = String.Empty;

        /// <summary>
        /// Source name written into transmitted bus events.
        /// <see cref="IEvent.Source"/>.
        /// </summary>
        public string SourceEntity { get; set; } = nameof(SRF.Network.OpenHab.Client);

        public bool FilterSource { get; set; } = true;

        public int ClientBufferSize { get; set; } = 1024;

        /// <summary>
        /// Waiting time in ms after an unsuccessful WebSocket reconnection attempt until trying again.
        /// </summary>
        public int ReconnectWaitTime { get; set; } = 5000;
    }
}
