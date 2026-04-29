using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Knx.Config;
using SRF.Knx.Core;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Dpt;
using SRF.Network.Knx.Hosting;
using SRF.Network.Knx.IpRouting;
using SRF.Network.Misc;
using SRF.Network.Udp;
using SRF.Network.Udp.Hosting;

namespace SRF.Network.Knx;

public static class ExtensionsHosting
{
    /// <summary>
    /// Adds the core KNX services to the service collection.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="SRF.Knx.Config.IKnxConfigFactory"/> and related config services (<c>AddKnxConfig</c>)</item>
    ///   <item>DPT factory chain: <see cref="SRF.Knx.Core.IDptFactory"/>, <c>IPdtEncoderFactory</c>, <c>IDptNumericInfoFactory</c> (<c>AddKnxCore</c>)</item>
    ///   <item><see cref="IDptResolver"/> → <see cref="KnxDptResolver"/></item>
    ///   <item><see cref="IKnxConnection"/> → <typeparamref name="TConnector"/></item>
    /// </list>
    /// <para>
    /// <b>Consumer must register separately:</b>
    /// <list type="bullet">
    ///   <item><c>IKnxMasterDataProvider</c>, which is required by
    ///   <see cref="SRF.Knx.Core.IDptFactory"/>. The <c>SRF.Knx.Core</c> library ships
    ///   <c>KnxMasterDataProvider</c> (loads from <c>KnxConfiguration.KnxMasterFolder</c>):
    ///   <code>services.AddSingleton&lt;IKnxMasterDataProvider, KnxMasterDataProvider&gt;();</code></item>
    ///   <item><see cref="SRF.Knx.Config.Domain.DomainConfiguration"/>, which is required by
    ///   <see cref="KnxDptResolver"/> and holds the ETS group address export:
    ///   <code>services.AddSingleton(sp => sp.GetRequiredService&lt;IKnxConfigFactory&gt;().GetDomainConfig());</code></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddKnxConfig();
        services.AddKnxCore();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IDptResolver, KnxDptResolver>();
        services.AddSingleton<IKnxConnection, TConnector>();
        services.TryAddSingleton<IKnxLibraryInitialization, KnxLibraryInitializationStub>();
        return services;
    }

    /// <summary>
    /// Adds the full KNX/IP Routing stack to the service collection, including UDP transport,
    /// connection management, message queuing, and KNX group message encoding/decoding.
    /// </summary>
    /// <remarks>
    /// Registers everything from <see cref="AddKnx{TConnector}"/> (with <see cref="KnxConnection"/>)
    /// plus the named UDP multicast client and connection manager:
    /// <list type="bullet">
    ///   <item><c>[FromKeyedServices(name)] IUdpMulticastClient</c></item>
    ///   <item><c>[FromKeyedServices(name)] IUdpMessageQueue</c></item>
    ///   <item><see cref="IKnxBus"/> → <see cref="KnxIpRoutingBus"/> (singleton, uses the keyed UDP services)</item>
    ///   <item>A <c>UdpConnectionManager</c> background service (drains the send queue, reconnects on failure)</item>
    /// </list>
    /// <para>
    /// Configuration sections (default, where <c>{name}</c> is the <paramref name="name"/> argument):
    /// <list type="bullet">
    ///   <item><c>Knx</c> — <see cref="SRF.Knx.Config.KnxConfiguration"/> (connection string, ETS file paths, …)</item>
    ///   <item><c>Udp:Connections:{name}</c> — <c>UdpMulticastOptions</c> (multicast address, port, …)</item>
    ///   <item><c>Udp:Connections:{name}:ConnectionManager</c> — <c>UdpConnectionManagerOptions</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Consumer prerequisites:</b> same as for <see cref="AddKnx{TConnector}"/> —
    /// <c>IKnxMasterDataProvider</c> and <see cref="SRF.Knx.Config.Domain.DomainConfiguration"/>
    /// must be registered separately before building the host.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">
    /// The connection name used as the DI key for the keyed UDP services and to derive the config section path.
    /// </param>
    /// <param name="configSection">
    /// Override for the UDP multicast config section. Defaults to <c>Udp:Connections:{name}</c>.
    /// </param>
    public static IServiceCollection AddKnxIpRouting(
        this IServiceCollection services,
        string name,
        string? configSection = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        services.AddUdpMulticastWithConnectionManager(name, configSection);

        // Infrastructure — all TryAdd, safe to call multiple times for multiple connections.
        services.AddKnxConfig();
        services.AddKnxCore();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDptResolver, KnxDptResolver>();
        services.TryAddSingleton<IKnxLibraryInitialization, KnxLibraryInitializationStub>();

        services.AddOptions<KnxIpRoutingOptions>()
            .BindConfiguration(KnxIpRoutingOptions.DefaultConfigSectionName);

        // KnxIpRoutingQueue: keyed by connection name
        services.AddKeyedSingleton<KnxIpRoutingQueue>(name, (sp, _) =>
        {
            var routingOpts  = sp.GetRequiredService<IOptions<KnxIpRoutingOptions>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new KnxIpRoutingQueue(routingOpts.Value, timeProvider);
        });
        services.AddKeyedSingleton<IKnxIpRoutingQueue>(
            name, (sp, _) => sp.GetRequiredKeyedService<KnxIpRoutingQueue>(name));

        // KnxIpRoutingSender: background service that drains the queue with rate limiting
        services.AddHostedService(sp =>
        {
            var queue      = sp.GetRequiredKeyedService<KnxIpRoutingQueue>(name);
            var udpClient  = sp.GetRequiredKeyedService<IUdpMulticastClient>(name);
            var logger     = sp.GetRequiredService<ILogger<KnxIpRoutingSender>>();
            return new KnxIpRoutingSender(name, queue, udpClient, logger);
        });

        // IKnxBus: keyed by connection name so each connection gets its own bus instance.
        services.AddKeyedSingleton<IKnxBus>(name, (sp, _) =>
        {
            var udpClient    = sp.GetRequiredKeyedService<IUdpMulticastClient>(name);
            var sendQueue    = sp.GetRequiredKeyedService<IKnxIpRoutingQueue>(name);
            var options      = sp.GetRequiredService<IOptions<KnxConfiguration>>();
            var routingOpts  = sp.GetRequiredService<IOptions<KnxIpRoutingOptions>>();
            var logger       = sp.GetRequiredService<ILogger<KnxIpRoutingBus>>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();
            return new KnxIpRoutingBus(udpClient, sendQueue, options, routingOpts, logger, timeProvider);
        });

        // IKnxConnection: keyed by connection name, explicitly wired to the keyed IKnxBus.
        services.AddKeyedSingleton<IKnxConnection>(name, (sp, _) =>
            new KnxConnection(
                sp.GetRequiredService<IKnxLibraryInitialization>(),
                sp.GetRequiredKeyedService<IKnxBus>(name),
                sp.GetRequiredService<IOptions<KnxConfiguration>>(),
                sp.GetRequiredService<ILogger<KnxConnection>>(),
                sp.GetRequiredService<IDptResolver>()));

        // Non-keyed IKnxConnection forwarding: allows IEnumerable<IKnxConnection> to aggregate
        // all named connections and keeps single-connection consumers (using IKnxConnection directly)
        // working without changes.
        services.AddSingleton<IKnxConnection>(
            sp => sp.GetRequiredKeyedService<IKnxConnection>(name));

        return services;
    }

    // -------------------------------------------------------------------------
    // IHostBuilder
    // -------------------------------------------------------------------------

    /// <summary>Adds the full KNX/IP Routing stack to the host builder.</summary>
    public static IHostBuilder AddKnxIpRouting(
        this IHostBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.ConfigureServices((_, services) => services.AddKnxIpRouting(name, configSection));
        return builder;
    }

    // -------------------------------------------------------------------------
    // IHostApplicationBuilder
    // -------------------------------------------------------------------------

    /// <summary>Adds the full KNX/IP Routing stack to the host application builder.</summary>
    public static IHostApplicationBuilder AddKnxIpRouting(
        this IHostApplicationBuilder builder,
        string name,
        string? configSection = null)
    {
        builder.Services.AddKnxIpRouting(name, configSection);
        return builder;
    }
}
