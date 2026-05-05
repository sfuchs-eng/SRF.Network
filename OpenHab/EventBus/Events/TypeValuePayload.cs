using System;
using System.Text.Json.Serialization;
using UnitsNet;

namespace SRF.Network.OpenHab.EventBus.Events
{
    /// <summary>
    /// See https://www.openhab.org/javadoc/latest/org/openhab/core/library/types/package-summary and those types
    /// for <see cref="Value"/> encoding variations.
    /// </summary>
    public class TypeValuePayload
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = String.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = String.Empty;
    }

    public static class TypeValuePayloadHelpers
    {
        private readonly static string ValueTypeSwitch = "OnOff";
        private readonly static string ValueTypeContact = "OpenClosed";

        public static bool IsTypeSwitch(this TypeValuePayload pl) => ValueTypeSwitch.Equals(pl.Type);
        public static bool IsTypeContact(this TypeValuePayload pl) => ValueTypeContact.Equals(pl.Type);

        public static bool IsOn(this TypeValuePayload pl) => IsTypeSwitch(pl) && "ON".Equals(pl.Value);
        public static bool IsOpen(this TypeValuePayload pl) => IsTypeContact(pl) && "OPEN".Equals(pl.Value);

        public static TypeValuePayload Set(this TypeValuePayload pl, IQuantity quantity)
        {
            pl.Type = "Quantity";
            pl.Value = quantity?.ToString() ?? String.Empty;
            return pl;
        }

        public static TypeValuePayload Set<NumericType>(this TypeValuePayload pl, NumericType number) where NumericType : struct
        {
            if (number is ItemStateSwitch)
                pl.Type = "OnOff";
            else if (number is ItemStateContact)
                pl.Type = "OpenClosed";
            else if (typeof(NumericType).IsEnum)
                throw new NotImplementedException($"Item value type for enum {nameof(NumericType)} is not known.");
            else
                pl.Type = "Decimal";

            pl.Value = number.ToString() ?? string.Empty;
            return pl;
        }

        /// <summary>
        /// Use with <see cref="ItemStateSwitch"/> and <see cref="ItemStateContact"/> or other item value type state enums.
        /// Numeric values are not supported as prior to .NET 7 the IParsable interface is not available for value types.
        /// </summary>
        public static NumericType Parse<NumericType>(this TypeValuePayload pl) where NumericType : struct
        {
            var myType = typeof(NumericType);
            if (myType.IsEnum)
                return (NumericType)Enum.Parse(myType, pl.Value);
            throw new ArgumentException($"Don't know how to deal with type {typeof(NumericType)} parsing.");
        }
    }
}
