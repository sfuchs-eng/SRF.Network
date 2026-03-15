# SRF.Network Project Guidelines

## Architecture

Multi-project solution of modular .NET networking libraries for home automation. Each library (Mqtt, Knx, Udp, WebSocket) is self-contained with clear interface abstractions and dependency injection support.

**Key Files**: 
- [Mqtt/MqttBrokerConnection.cs](Mqtt/MqttBrokerConnection.cs) - Reference implementation following all patterns
- [Udp/UdpMulticastClient.cs](Udp/UdpMulticastClient.cs) - Recent complete implementation
- [Udp/UdpMessageQueue.cs](Udp/UdpMessageQueue.cs) - Singleton/BackgroundService split pattern
- [Knx/IKnxConnection.cs](Knx/IKnxConnection.cs) - Interface design pattern

## Code Style

- **Target**: .NET 10.0, C# 13, nullable enabled
- **Naming**: PascalCase for all public members, _camelCase for private fields
- **Usings**: Global usings in `globalusings.cs` per project (System, System.Net.*, Microsoft.Extensions.*)
- **Documentation**: XML comments on all public APIs
- **Field initialization**: Use field initializers or constructor, avoid nullable fields without null checks

## Dependency Injection Pattern

**Options Classes** ([UdpMulticastOptions.cs](Udp/UdpMulticastOptions.cs)):
```csharp
public class XxxOptions
{
    public const string DefaultConfigSectionName = "Category:Subcategory";
    // Properties with default values
}
```

**Hosting Extensions** ([Udp/Hosting/UdpMulticastHostingExtensions.cs](Udp/Hosting/UdpMulticastHostingExtensions.cs)):
```csharp
public static IServiceCollection AddXxx(this IServiceCollection services, string? configSection = null)
{
    services.AddOptions<XxxOptions>().BindConfiguration(configSection ?? XxxOptions.DefaultConfigSectionName);
    services.AddSingleton<IXxx, Xxx>();
    return services;
}
```

Also provide extensions for `IHostBuilder` and `IHostApplicationBuilder` - see reference implementations.

## Interface Design

- Interface per major component: `IXxxConnection`, `IXxxClient`
- Event-driven communication: `EventHandler<CustomEventArgs>` for async notifications
- Async-first: All I/O returns `Task` with optional `CancellationToken`
- Disposable: Implement `IDisposable` for resource cleanup
- Connection lifecycle: `ConnectAsync()`, `DisconnectAsync()`, `IsConnected` property

**Example**: [IUdpMulticastClient.cs](Udp/IUdpMulticastClient.cs)

## Logging

Use structured logging with `ILogger<T>`:
- **Trace**: Message-level details (send/receive individual packets)
- **Debug**: Lifecycle events (loops starting/stopping)
- **Information**: Connection state changes, major operations
- **Warning**: Retryable errors, cleanup issues
- **Error**: Connection failures, unrecoverable errors

Example: `_logger.LogInformation("Connected to {Address}:{Port}", address, port)`

## Event-Driven Patterns

**Custom EventArgs** ([UdpMessageReceivedEventArgs.cs](Udp/UdpMessageReceivedEventArgs.cs)):
```csharp
public class XxxEventArgs : EventArgs
{
    public TypeA Property { get; }
    public XxxEventArgs(TypeA property) => Property = property;
}
```

Events: `public event EventHandler<XxxEventArgs>? EventName;`

## Async Patterns

- Background receive loops: `Task.Run(() => ReceiveLoopAsync(cancellationToken))`
- Cancellation: Use `CancellationTokenSource` for graceful shutdown
- Blocking collections: `BlockingCollection<T>` for queues with `Add()` and `Take(cancellationToken)`
- Thread safety: Lock critical sections with `lock (_lock)` for state changes

**Reference**: [UdpMulticastClient.cs](Udp/UdpMulticastClient.cs) lines 165-217

## Error Handling

- Validate parameters: `ArgumentNullException.ThrowIfNull(param)` or manual checks with meaningful messages
- Catch specific exceptions: `SocketException`, `OperationCanceledException`
- Log before rethrowing: Log context, then `throw;` to preserve stack trace
- Connection errors: Set `IsConnected = false`, raise `ConnectionStatusChanged` event

## Build and Test

```bash
# Build entire solution
dotnet build SRF.Network.sln

# Build specific project
dotnet build ProjectFolder/SRF.Network.ProjectName.csproj

# Run tests (NUnit framework)
dotnet test Test/SRF.Network.Test.csproj
```

## Project Structure

Each library follows this pattern:
```
LibraryName/
  ├── IXxxClient.cs          # Main interface
  ├── XxxClient.cs           # Implementation
  ├── XxxOptions.cs          # Configuration
  ├── XxxEventArgs.cs        # Event arguments
  ├── globalusings.cs        # Global using directives
  ├── README.md              # Usage documentation
  ├── SRF.Network.Xxx.csproj # Project file
  └── Hosting/
      └── XxxHostingExtensions.cs
```

## Required NuGet Packages

All projects typically need:
- `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.*)
- `Microsoft.Extensions.Hosting.Abstractions` (10.0.*)
- `Microsoft.Extensions.Logging.Abstractions` (10.0.*)
- `Microsoft.Extensions.Options.ConfigurationExtensions` (10.0.*)

## Background Services

For hosted services ([Mqtt/Hosting/ConnectionManager.cs](Mqtt/Hosting/ConnectionManager.cs)):
- Implement `IHostedService` or derive from `BackgroundService`
- Register with `services.AddHostedService<TService>()`
- Handle cancellation in `ExecuteAsync(CancellationToken)`
- Clean up in `StopAsync(CancellationToken)`

**BackgroundService / Singleton split pattern** ([Udp/UdpMessageQueue.cs](Udp/UdpMessageQueue.cs), [Udp/Hosting/UdpConnectionManager.cs](Udp/Hosting/UdpConnectionManager.cs)):

When a `BackgroundService` must also be injectable by consumers (e.g. to enqueue work), split into two classes:
- **Singleton** (`XxxQueue`/`XxxService`) — injectable, holds state/queue, registered as both its concrete type and its interface
- **BackgroundService** (`XxxConnectionManager`) — injects the concrete singleton to access `internal` members; consumers never inject this directly

Registration pattern that gives both `IXxxQueue` and `XxxQueue` the same singleton instance:
```csharp
services.AddSingleton<XxxQueue>();                                              // concrete type for BackgroundService
services.AddSingleton<IXxxQueue>(sp => sp.GetRequiredService<XxxQueue>());     // same instance for consumers
services.AddHostedService<XxxConnectionManager>();
```

## Security Note

The Knx library supports KNX communication security (key rings, sequence counters) - see commented code in [KnxConnection.cs](Knx/Connection/KnxConnection.cs) lines 34-57 for future implementation reference.
