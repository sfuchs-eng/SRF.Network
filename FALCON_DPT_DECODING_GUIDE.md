# Falcon DPT (DatapointType) Decoding Guide

This guide shows how to use the Knx.Falcon library's DPT (DatapointType) classes to decode KNX payload values.

## Required Imports

```csharp
using Knx.Falcon;                                    // Core Falcon types
using Knx.Falcon.ApplicationData;                    // DptFactory
using Knx.Falcon.ApplicationData.DatapointTypes;    // DptSimple, DptComplex, etc.
```

## 1. Creating DPT Objects with DptFactory

### Using DptFactory.Default

The `DptFactory` is used to get DPT objects by their main and sub type numbers:

```csharp
// Get DptFactory instance
private readonly DptFactory dptFactory = DptFactory.Default;

// Create a DPT object for a specific type (e.g., DPT 9.001 - Temperature)
// DPT 9 = main type, 001 = sub type
var dpt = dptFactory.Get(main: 9, sub: 1);
```

### Checking DPT Type

```csharp
// Check if the retrieved DPT is a simple type (most common for value decoding)
if (dpt != null && dpt is DptSimple simpleDpt)
{
    // Access numeric information like unit
    string? unit = simpleDpt.NumericInfo.Unit;  // e.g., "°C" for temperature
    Console.WriteLine($"DPT Unit: {unit}");
}
else if (dpt is DptComplex complexDpt)
{
    // Handle complex DPT types
}
```

## 2. Getting Raw KNX Payload Values

When KNX messages are received, they contain byte arrays representing the encoded values:

```csharp
// From a KnxMessageContext (used in message handlers)
KnxMessageContext context = new KnxMessageContext(groupEventArgs);

// Get the raw byte array payload
byte[] rawPayload = context.RawValue;
// Or from GroupEventArgs directly:
byte[] rawPayload = groupEventArgs?.Value.Value ?? new byte[0];

// Example payload
Console.WriteLine($"Raw payload: {string.Join(", ", rawPayload.Select(b => $"0x{b:X2}"))}");
// Output: Raw payload: 0x0C, 0x1A
```

## 3. Accessing DPT Properties

### DptSimple Numeric Information

For simple DPTs with numeric values, access the `NumericInfo` property:

```csharp
var dpt = dptFactory.Get(9, 1);  // Temperature DPT

if (dpt is DptSimple simpleDpt)
{
    // Access numeric information
    var numericInfo = simpleDpt.NumericInfo;
    
    Console.WriteLine($"Unit: {numericInfo.Unit}");                    // e.g., "°C"
    Console.WriteLine($"Minimum: {numericInfo.Minimum}");              // e.g., -273.15
    Console.WriteLine($"Maximum: {numericInfo.Maximum}");              // e.g., 327.67
    Console.WriteLine($"Decimal Places: {numericInfo.DecimalPlaces}");  // e.g., 2
}
```

## 4. Complete KNX Message Handler Example

This example demonstrates how to receive KNX messages and work with their payloads:

```csharp
using Knx.Falcon;
using Knx.Falcon.ApplicationData;
using Knx.Falcon.ApplicationData.DatapointTypes;

public class KnxMessageHandler
{
    private readonly DptFactory dptFactory = DptFactory.Default;

    public void HandleKnxMessage(GroupEventArgs groupEventArgs)
    {
        // 1. Get message information
        var sourceAddress = groupEventArgs.SourceAddress;
        var destinationAddress = groupEventArgs.DestinationAddress;
        var rawPayload = groupEventArgs.Value.Value;

        Console.WriteLine($"Message from {sourceAddress} to {destinationAddress}");
        Console.WriteLine($"Raw payload: {string.Join(",", rawPayload.Select(b => $"0x{b:X2}"))}");

        // 2. Get the appropriate DPT for this group address
        // In real scenarios, you would determine DPT from configuration
        int dptMain = 9;    // From ETS configuration
        int dptSub = 1;     // From ETS configuration

        var dpt = dptFactory.Get(dptMain, dptSub);

        // 3. Process the DPT
        if (dpt != null && dpt is DptSimple simpleDpt)
        {
            Console.WriteLine($"DPT: {simpleDpt}");
            
            // Access DPT metadata
            if (simpleDpt.NumericInfo != null)
            {
                Console.WriteLine($"  Unit: {simpleDpt.NumericInfo.Unit}");
                Console.WriteLine($"  Min: {simpleDpt.NumericInfo.Minimum}");
                Console.WriteLine($"  Max: {simpleDpt.NumericInfo.Maximum}");
                Console.WriteLine($"  Decimals: {simpleDpt.NumericInfo.DecimalPlaces}");
            }
        }
    }
}
```

## 5. Integration with KnxConnection Events

When using SRF.Network's KnxConnection wrapper:

```csharp
public class Worker : BackgroundService
{
    private readonly IKnxConnection knxConnection;
    private readonly DptFactory dptFactory = DptFactory.Default;

    private void KnxMessageReceivedHandler(object? sender, KnxMessageReceivedEventArgs e)
    {
        var context = e.KnxMessageContext;
        
        // Extract message information
        var tgtAddr = context.GroupEventArgs?.DestinationAddress.ToString();
        var srcAddr = context.GroupEventArgs?.SourceAddress.ToString();
        var rawPayload = context.RawValue;

        // Log raw payload
        var payloadHex = string.Join(',', 
            rawPayload.Select(b => $"0x{b.ToString("X2")}"));
        Console.WriteLine($"- from {srcAddr} to {tgtAddr}: {payloadHex}");

        // If you have DPT information from configuration:
        if (TryGetDptForAddress(tgtAddr, out int main, out int sub))
        {
            var dpt = dptFactory.Get(main, sub);
            if (dpt is DptSimple simpleDpt)
            {
                Console.WriteLine($"  DPT Unit: {simpleDpt.NumericInfo?.Unit}");
            }
        }
    }

    private bool TryGetDptForAddress(string? address, out int main, out int sub)
    {
        // In a real implementation, look up DPT from configuration
        main = 0;
        sub = 0;
        return false;
    }
}
```

## 6. DPT Type Hierarchy

The Falcon SDK provides different DPT base types:

```csharp
// Common DPT class hierarchy:
// DPT (base)
//  ├── DptSimple      (for simple numeric types)
//  ├── DptComplex     (for complex structured types)
//  └── DptContainer   (for container types)

var dpt = dptFactory.Get(9, 1);  // Returns DptSimple

// Check the actual type
var typeName = dpt?.GetType().Name;
Console.WriteLine($"DPT Type: {typeName}");  // e.g., "DptSimple"
```

## 7. Accessing Configuration from ETS Data

Example from SRF.Knx.Config showing DPT usage with ETS data:

```csharp
using Knx.Falcon.ApplicationData;
using Knx.Falcon.ApplicationData.DatapointTypes;

public class OpenHabKnxConfigFactory
{
    private readonly DptFactory dptFactory = DptFactory.Default;

    private void ProcessGroupAddress(EtsGroupAddressConfig ets)
    {
        // Check if the group address has a valid DPT configured in ETS
        if (ets.HasValidDPT && !ets.DPT.IsMainOnly)
        {
            // Get the DPT from the factory
            var dpt = dptFactory.Get(ets.DPT.Main, ets.DPT.Sub);
            
            if (dpt != null && dpt is DptSimple simpleDpt)
            {
                // Extract unit information
                string knxUnit = simpleDpt.NumericInfo.Unit;
                
                // Find matching dimension in unit system config
                var dimension = unitSystemConfig.DimensionLookups
                    .FirstOrDefault(dlut => dlut.Units.Any(u => u.Equals(knxUnit)));
                
                Console.WriteLine($"GA {ets.Label}: DPT {ets.DPT.EtsFormat}, Unit: {knxUnit}");
            }
        }
    }
}
```

## 8. Common DPT Examples

```csharp
// Some common DPT types you might encounter:

// Boolean / Bit (DPT 1.x)
var dpt1_001 = dptFactory.Get(1, 1);  // Binary value (on/off)

// Signed Integer (DPT 5.x)
var dpt5_001 = dptFactory.Get(5, 1);  // Percentage 0-100%
var dpt5_003 = dptFactory.Get(5, 3);  // Angle 0-360°

// Floating Point (DPT 9.x)
var dpt9_001 = dptFactory.Get(9, 1);  // Temperature °C
var dpt9_007 = dptFactory.Get(9, 7);  // Humidity %

// Unsigned 16-bit (DPT 7.x)
var dpt7_001 = dptFactory.Get(7, 1);  // Unsigned integer

// 4-byte Signed (DPT 13.x)
var dpt13_001 = dptFactory.Get(13, 1);  // Signed 32-bit integer

// Time (DPT 10.x)
var dpt10_001 = dptFactory.Get(10, 1);  // Time of day

// Date (DPT 11.x)
var dpt11_001 = dptFactory.Get(11, 1);  // Date
```

## 9. Error Handling

```csharp
public void SafelyDecodeDptValue(int main, int sub, byte[] payload)
{
    try
    {
        var dpt = dptFactory.Get(main, sub);
        
        if (dpt == null)
        {
            Console.WriteLine($"DPT {main}.{sub} not found in factory");
            return;
        }

        if (dpt is DptSimple simpleDpt)
        {
            // Successfully got a simple DPT
            Console.WriteLine($"DPT: {simpleDpt}, Unit: {simpleDpt.NumericInfo?.Unit}");
        }
        else if (dpt is DptComplex complexDpt)
        {
            Console.WriteLine($"Complex DPT: {complexDpt}");
            // Handle complex types appropriately
        }
        else
        {
            Console.WriteLine($"Unknown DPT type: {dpt.GetType().Name}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing DPT {main}.{sub}: {ex.Message}");
    }
}
```

## Key Points

1. **DptFactory.Get()** - Main entry point for creating DPT objects by main and sub type
2. **DptSimple** - Most common type for simple numeric values with unit information
3. **NumericInfo** - Provides metadata like unit, min/max values, and decimal places
4. **Byte arrays** - Raw payload data from KNX messages (accessible via `GroupEventArgs.Value.Value`)
5. **Configuration** - DPT types should come from ETS configuration to properly interpret message payloads

## Related Resources

- **Knx.Falcon.Sdk** - NuGet package: https://www.nuget.org/packages/Knx.Falcon.Sdk
- **KNX Falcon SDK Documentation** - https://support.knx.org/hc/en-us/sections/4410811049618-Get-Started
- **SRF.Knx.Config** - Configuration handling from ETS to Falcon SDK in this project
