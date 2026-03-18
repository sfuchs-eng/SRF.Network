using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SRF.Network.Udp.Hosting;

/// <summary>
/// Extension methods for registering named UDP multicast services with the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// Each connection is identified by a <c>name</c> string that acts as the DI key. The full
/// configuration path for a named connection follows the convention
/// <c>Udp:Connections:{name}</c> (multicast options) and
/// <c>Udp:Connections:{name}:ConnectionManager</c> (connection-manager options).
/// </para>
/// <para>
/// Consumers retrieve named services using the <c>[FromKeyedServices("name")]</c> attribute:
/// <code>
/// public MyService([FromKeyedServices("Knx")] IUdpMulticastClient knxClient,
///                  [FromKeyedServices("Knx")] IUdpMessageQueue    knxQueue) { }
/// </code>
/// </para>
/// <para>
/// Multiple independent connections can be registered in a single host by calling the
/// extension methods once per named connection.
/// </para>
/// </remarks>
public static class UdpMulticastHostingExtensions
{
    // -------------------------------------------------------------------------
    // IServiceCollection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a named UDP multicast client to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The connection name used as the DI key and to resolve the config section.</param>
    /// <param name="configSection">
    /// Explicit configuration section path. Defaults to <c>Udp:Connections:{name}</c>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUdpMulticast(
        this IServiceCollection services,
        string name,
        string? configSection = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        string section = configSection ?? $"{UdpMulticastOptions.DefaultConfigSectionName}:{name}";

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<UdpMulticastOptions>(name)
            .BindConfiguration(section);

        // Keyed singleton: each name gets its own UdpMulticastClient instance.
        // Factory reads the named options snapshot so the correct address/port are used.
        services.AddKeyedSingleton<IUdpMulticastClient>(name, (sp, _) =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<UdpMulticastOptions>>();
            var opts    = Options.Create(monitor.Get(name));
            var logger  = sp.GetRequiredService<ILogger<UdpMulticastClient>>();
            var tp      = sp.GetRequiredService<TimeProvider>();
            return new UdpMulticastClient(opts, logger, tp);
        });

        return services;
    }

    /// <summary>
    /// Adds a named UDP multicast client together with automatic connection management and
    /// a message queue to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two keyed singletons are registered on the same <see cref="UdpMessageQueue"/> instance:
    /// <list type="bullet">
    ///   <item><c>[FromKeyedServices(name)] IUdpMessageQueue</c> — inject into consumers to enqueue messages.</item>
    ///   <item><c>[FromKeyedServices(name)] UdpMessageQueue</c> — concrete type, used internally by the connection manager.</item>
    /// </list>
    /// A <see cref="UdpConnectionManager"/> background service is also registered as
    /// <see cref="IHostedService"/>. Multiple calls with different names each produce their own
    /// independent connection-manager instance.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The connection name used as the DI key and to resolve the config section.</param>
    /// <param name="configSection">
    /// Explicit configuration section path for <see cref="UdpMulticastOptions"/>.
    /// Defaults to <c>Udp:Connections:{name}</c>.
    /// Connection-manager options are always read from <c>{resolvedSection}:ConnectionManager</c>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUdpMulticastWithConnectionManager(
        this IServiceCollection services,
        string name,
        string? configSection = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        string multicastSection = configSection ?? $"{UdpMulticastOptions.DefaultConfigSectionName}:{name}";
        string managerSection   = $"{multicastSection}:{UdpConnectionManagerOptions.SubSectionName}";

        services.AddUdpMulticast(name, multicastSection);

        services.AddOptions<UdpConnectionManagerOptions>(name)
            .BindConfiguration(managerSection);

        // Concrete keyed singleton — UdpConnectionManager injects this to access internal members.
        services.AddKeyedSingleton<UdpMessageQueue>(name, (sp, _) =>
        {
            var client = sp.GetRequiredKeyedService<IUdpMulticastClient>(name);
            var logger = sp.GetRequiredService<ILogger<UdpMessageQueue>>();
            var tp     = sp.GetRequiredService<TimeProvider>();
            return new UdpMessageQueue(client, logger, tp);
        });

        // Interface keyed singleton — consumers inject this to enqueue messages.
        // Resolves the same instance as the concrete registration above.
        services.AddKeyedSingleton<IUdpMessageQueue>(name,
            (sp, _) => sp.GetRequiredKeyedService<UdpMessageQueue>(name));

        // Register the connection manager as IHostedService using a factory so that
        // multiple named managers can coexist — AddHostedService<T>() would collide on the
        // same closed generic type when called more than once.
        services.Add(ServiceDescriptor.Singleton<IHostedService>(sp =>
        {
            var queue   = sp.GetRequiredKeyedService<UdpMessageQueue>(name);
            var monitor = sp.GetRequiredService<IOptionsMonitor<UdpConnectionManagerOptions>>();
            var opts    = Options.Create(monitor.Get(name));
            var logger  = sp.GetRequiredService<ILogger<UdpConnectionManager>>();
            var tp      = sp.GetRequiredService<TimeProvider>();
            return new UdpConnectionManager(name, queue, opts, logger, tp);
        }));

        return services;
    }

    // -------------------------------------------------------------------------
    // IHostBuilder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a named UDP multicast client to the host builder.
    /// </summary>
    public static IHostBuilder AddUdpMulticast(
        this IHostBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.ConfigureServices((_, services) => services.AddUdpMulticast(name, configSection));
        return builder;
    }

    /// <summary>
    /// Adds a named UDP multicast client with automatic connection management to the host builder.
    /// </summary>
    public static IHostBuilder AddUdpMulticastWithConnectionManager(
        this IHostBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.ConfigureServices((_, services) => services.AddUdpMulticastWithConnectionManager(name, configSection));
        return builder;
    }

    // -------------------------------------------------------------------------
    // IHostApplicationBuilder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a named UDP multicast client to the host application builder.
    /// </summary>
    public static IHostApplicationBuilder AddUdpMulticast(
        this IHostApplicationBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.Services.AddUdpMulticast(name, configSection);
        return builder;
    }

    /// <summary>
    /// Adds a named UDP multicast client with automatic connection management to the host application builder.
    /// </summary>
    public static IHostApplicationBuilder AddUdpMulticastWithConnectionManager(
        this IHostApplicationBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.Services.AddUdpMulticastWithConnectionManager(name, configSection);
        return builder;
    }
}
