using System;
using System.Threading;
using System.Threading.Tasks;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using System.Text.Json;

namespace SRF.Network.OpenHab
{
    public interface IEventBusClient
    {
        /// <summary>
        /// Connection about to be established, established or about to close but not fully closed yet.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Connects, starts the keepalive (aka ping-pong watchdog) and receives events which are sent to a blocking queue.
        /// Recieved events can be processed registering to <see cref="EventReceived"/>.
        /// The function only completes after its cancelled, the connection closes or there's no pong on the ping.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// for processing received <see cref="IEvent"/>.
        /// </summary>
        event EventHandler<EventReceivedEventArgs> EventReceived;
       
        /// <summary>
        /// Gets the event factory used by the <see cref="IEventBusClient"/> implementation at hands.
        /// </summary>
        IEventFactory EventFactory { get; }

        /// <summary>
        /// Enqueues an <see cref="IEvent"/> for transmitting.
        /// </summary>
        void EnqueueTransmit(IEvent sendEvent);

        /// <summary>
        /// Instanciates an <see cref="EventBus.Events.ItemEventTypeValue"/> of type <see cref="EventBus.EventType.ItemCommandEvent"/>
        /// with the defined <paramref name="state"/> and passes it to <see cref="EnqueueTransmit(IEvent)"/>.
        /// </summary>
        void Command<ItemStateType>(string itemName, ItemStateType state) where ItemStateType : struct;

        /// <summary>
        /// Closes the connection, stops the watchdog
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task CloseAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Send an <see cref="IEvent"/> directly, bypassing the queue.
        /// For special cases only. <see cref="EnqueueTransmit(IEvent)"/> should be used in most cases.
        /// </summary>
        Task SendAsync(IEvent sendEvent, CancellationToken cancellationToken);
        Task SendAsync(string packet, CancellationToken cancellationToken);
    }
}
