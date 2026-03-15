using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SRF.Network.Udp.Hosting;

/// <summary>
/// Extension methods for registering UDP multicast services with the dependency injection container.
/// </summary>
public static class UdpMulticastHostingExtensions
{
    /// <summary>
    /// Adds UDP multicast services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configSection">The configuration section name. Defaults to "Udp:Multicast".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUdpMulticast(this IServiceCollection services, string? configSection = null)
    {
        services.AddOptions<UdpMulticastOptions>()
            .BindConfiguration(configSection ?? UdpMulticastOptions.DefaultConfigSectionName);

        services.AddSingleton<IUdpMulticastClient, UdpMulticastClient>();

        return services;
    }

    /// <summary>
    /// Adds UDP multicast services with automatic connection management to the service collection.
    /// <para>
    /// Registers two singletons on the same <see cref="UdpMessageQueue"/> instance:
    /// <list type="bullet">
    ///   <item><see cref="IUdpMessageQueue"/> — inject into consumers to enqueue messages.</item>
    ///   <item><see cref="UdpMessageQueue"/> — injected by <see cref="UdpConnectionManager"/> to drain the queue.</item>
    /// </list>
    /// Consumers who want to receive messages should inject <see cref="IUdpMulticastClient"/> directly.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="multicastConfigSection">The multicast configuration section name. Defaults to "Udp:Multicast".</param>
    /// <param name="managerConfigSection">The connection manager configuration section name. Defaults to "Udp:ConnectionManager".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUdpMulticastWithConnectionManager(
        this IServiceCollection services,
        string? multicastConfigSection = null,
        string? managerConfigSection = null)
    {
        services.AddUdpMulticast(multicastConfigSection);

        services.AddOptions<UdpConnectionManagerOptions>()
            .BindConfiguration(managerConfigSection ?? UdpConnectionManagerOptions.DefaultConfigSectionName);

        // Register the queue as its concrete type so UdpConnectionManager can take it directly
        // and access internal Take()/CompleteAdding(). Also forward IUdpMessageQueue to the
        // same singleton so injection by interface resolves the same instance.
        services.AddSingleton<UdpMessageQueue>();
        services.AddSingleton<IUdpMessageQueue>(sp => sp.GetRequiredService<UdpMessageQueue>());

        services.AddHostedService<UdpConnectionManager>();

        return services;
    }

    /// <summary>
    /// Adds UDP multicast services to the host builder.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="configSection">The configuration section name. Defaults to "Udp:Multicast".</param>
    /// <returns>The host builder for chaining.</returns>
    public static IHostBuilder AddUdpMulticast(this IHostBuilder builder, string? configSection = null)
    {
        builder.ConfigureServices((context, services) =>
        {
            services.AddUdpMulticast(configSection);
        });

        return builder;
    }

    /// <summary>
    /// Adds UDP multicast services with automatic connection management to the host builder.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="multicastConfigSection">The multicast configuration section name. Defaults to "Udp:Multicast".</param>
    /// <param name="managerConfigSection">The connection manager configuration section name. Defaults to "Udp:ConnectionManager".</param>
    /// <returns>The host builder for chaining.</returns>
    public static IHostBuilder AddUdpMulticastWithConnectionManager(
        this IHostBuilder builder,
        string? multicastConfigSection = null,
        string? managerConfigSection = null)
    {
        builder.ConfigureServices((context, services) =>
        {
            services.AddUdpMulticastWithConnectionManager(multicastConfigSection, managerConfigSection);
        });

        return builder;
    }

    /// <summary>
    /// Adds UDP multicast services to the host application builder.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configSection">The configuration section name. Defaults to "Udp:Multicast".</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddUdpMulticast(this IHostApplicationBuilder builder, string? configSection = null)
    {
        builder.Services.AddUdpMulticast(configSection);
        return builder;
    }

    /// <summary>
    /// Adds UDP multicast services with automatic connection management to the host application builder.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="multicastConfigSection">The multicast configuration section name. Defaults to "Udp:Multicast".</param>
    /// <param name="managerConfigSection">The connection manager configuration section name. Defaults to "Udp:ConnectionManager".</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddUdpMulticastWithConnectionManager(
        this IHostApplicationBuilder builder,
        string? multicastConfigSection = null,
        string? managerConfigSection = null)
    {
        builder.Services.AddUdpMulticastWithConnectionManager(multicastConfigSection, managerConfigSection);
        return builder;
    }
}
