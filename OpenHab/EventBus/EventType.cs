using System;
using System.Text.Json.Serialization;

namespace SRF.Network.OpenHab.EventBus
{
    /// <summary>
    /// OpenHAB WebSocket API Event Types
    /// https://www.openhab.org/docs/configuration/websocket.html
    /// </summary>
    public enum EventType
    {
        /// <summary>
        /// Event type was not defined
        /// </summary>
        Undefined,

        /// <summary>
        /// It's an event type that is not mapped in the enum and/or no class is associated to it.
        /// Handle by default with <see cref="Event"/>.
        /// </summary>
        Unrecognized,

        /*=== Item events ===*/

        /// <summary>
        /// An item has been added to the item registry.
        /// Topic: openhab/items/{itemName}/added
        /// </summary>
        ItemAddedEvent,

        /// <summary>
        /// An item has been removed from the item registry.
        /// Topic: openhab/items/{itemName}/removed
        /// </summary>
        ItemRemovedEvent,

        /// <summary>
        /// An item has been updated in the item registry.
        /// Topic: openhab/items/{itemName}/updated
        /// </summary>
        ItemUpdatedEvent,

        /// <summary>
        /// A command is sent to an item via a channel.
        /// Topic: openhab/items/{itemName}/command
        /// </summary>
        ItemCommandEvent,

        /// <summary>
        /// The state of an item is updated.
        /// Topic: openhab/items/{itemName}/state
        /// </summary>
        ItemStateEvent,

        /// <summary>
        /// The state of an item predicted to be updated.
        /// Topic: openhab/items/{itemName}/statepredicted
        /// </summary>
        ItemStatePredictedEvent,

        /// <summary>
        /// The item state changed event.
        /// Topic: openhab/items/{itemName}/statechanged
        /// </summary>
        ItemStateChangedEvent,

        /// <summary>
        /// The state of a group item has changed through a member.
        /// openhab/items/{itemName}/{memberName}/statechanged
        /// </summary>
        GroupItemStateChangedEvent,


        /*=== Thing events ===*/

        /// <summary>
        /// A thing has been added to the thing registry.
        /// openhab/things/{thingUID}/added
        /// </summary>
        ThingAddedEvent,

        /// <summary>
        /// A thing has been removed from the thing registry.
        /// openhab/things/{thingUID}/removed
        /// </summary>
        ThingRemovedEvent,

        /// <summary>
        /// A thing has been updated in the thing registry.
        /// openhab/things/{thingUID}/updated
        /// </summary>
        ThingUpdatedEvent,

        /// <summary>
        /// The status of a thing is updated.
        /// openhab/things/{thingUID}/status
        /// </summary>
        ThingStatusInfoEvent,

        /// <summary>
        /// The status of a thing changed.
        /// openhab/things/{thingUID}/statuschanged
        /// </summary>
        ThingStatusInfoChangedEvent,

        /*=== Inbox events ===*/

        /*=== Link events ===*/

        /*=== Channel events ===*/

        /// <summary>
        /// A dynamic CommandDescription or StateDescription has changed.
        /// openhab/channels/{channelUID}/descriptionchanged
        /// </summary>
        ChannelDescriptionChangedEvent,

        /// <summary>
        /// A channel has been triggered.
        /// openhab/channels/{channelUID}/triggered
        /// </summary>
        ChannelTriggeredEvent,

        /*=== Rule events ===*/
        RuleStatusInfoEvent,
        RuleAddedEvent,
        RuleRemovedEvent,

        /*=== WebSocket events ===*/

        /// <summary>
        /// openhab/websocket/response/failed
        /// See eventID and payload fields.
        /// </summary>
        WebSocketEvent,
    }

    public static class EventTypeHelper
    {
        public static string GetTypeName(this EventType id) => id.ToString();
    }

    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        UseStringEnumConverter = true,
        AllowOutOfOrderMetadataProperties = true,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    )]
    [JsonSerializable(typeof(EventType))]
    internal partial class EventTypeMetadataOnlyContext : JsonSerializerContext
    {
    }
}
