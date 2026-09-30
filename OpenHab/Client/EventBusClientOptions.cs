using System;
namespace SRF.Network.OpenHab.Client
{
    public class EventBusClientOptions
    {
        /// <summary>
        /// Enables connecting to an OpenHAB instance.
        /// Set to <see langword="false"/> for offline testing while still instantiating related services.
        /// </summary>
        public bool Enable { get; set; } = true;

        public bool EnableWebSocket { get; set; } = true;

        /// <summary>
        /// Complete OpenHAB server websocket URI used with <see cref="System.Net.WebSockets.ClientWebSocket"/>.
        /// Use "wss://localhost:8443/ws" for SSL and "ws://localhost:8080/ws" for a plain connection to default ports.
        /// </summary>
        public string WebSocket { get; set; } = "ws://localhost:8080/ws/events";

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

        /// <summary>
        /// When enabled, the client requests the server to filter events to the configured SourceEntity.
        /// This is opt-in because defaulting it to true suppresses normal item updates from other sources.
        /// </summary>
        public bool FilterSource { get; set; } = false;

        public int ClientBufferSize { get; set; } = 1024;

        /// <summary>
        /// Waiting time in ms after an unsuccessful WebSocket reconnection attempt until trying again.
        /// </summary>
        public int ReconnectWaitTime { get; set; } = 5000;

        /// <summary>
        /// Enables insecure TLS certificate validation for self-signed or otherwise untrusted certificates.
        /// Use only for trusted private networks.
        /// </summary>
        public bool AllowInsecureTls { get; set; } = false;
    }
}
