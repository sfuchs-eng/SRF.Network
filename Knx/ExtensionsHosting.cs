using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.Knx.Connection;
using SRF.Network.Knx.Dpt;
using SRF.Network.Knx.Hosting;
using SRF.Network.Knx.IpRouting;
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
    ///   <item><see cref="SRF.Knx.Config.IKnxConfigFactory"/> and related config services via <c>AddKnxConfig</c>,
    ///   including <see cref="SRF.Knx.Config.Domain.DomainConfiguration"/> (loaded from the ETS GA export file)
    ///   and <see cref="SRF.Knx.Core.IKnxMasterDataProvider"/> → <see cref="SRF.Knx.Config.KnxMasterDataProvider"/>.</item>
    ///   <item>DPT factory chain: <see cref="SRF.Knx.Core.IDptFactory"/>, <c>IPdtEncoderFactory</c>, <c>IDptNumericInfoFactory</c> via <c>AddKnxCore</c>.</item>
    ///   <item><see cref="IDptResolver"/> → <see cref="KnxDptResolver"/></item>
    ///   <item><see cref="IKnxConnection"/> → <typeparamref name="TConnector"/></item>
    /// </list>
    /// <para>
    /// All registrations use <c>TryAdd</c> and are safe to call multiple times (e.g. for multiple connections).
    /// A consumer can override any registration before calling this method.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddKnxConfig();
        //services.AddKnxCore(); called by AddKnxConfig, which also registers the IKnxSystemConfiguration and IDptResolver that depend on core services

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
    ///   <item><c>Knx:Connections:{name}</c> — <see cref="KnxConnectionOptions"/> (admin-facing KNX/IP connection options)</item>
    ///   <item><c>Udp:Connections:{name}</c> — retained UDP transport options (populated from effective KNX options)</item>
    /// </list>
    /// </para>
    /// <para>
    /// All infrastructure registrations use <c>TryAdd</c> and are safe to call multiple times.
    /// A consumer can override any registration (e.g. <c>IKnxMasterDataProvider</c>) before calling this method.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">
    /// The connection name used as the DI key for the keyed UDP services and to derive the config section path.
    /// </param>
    /// <param name="configSection">
    /// Override for the KNX connection config section. Defaults to <c>Knx:Connections:{name}</c>.
    /// </param>
    public static IServiceCollection AddKnxIpRouting(
        this IServiceCollection services,
        string name,
        string? configSection = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        string knxSection = configSection ?? $"{KnxConnectionOptions.DefaultConfigSectionName}:{name}";

        services.AddOptions<KnxConnectionOptions>(name)
            .BindConfiguration(knxSection);

        // Keep UDP transport options in place and project KNX admin-facing options into them.
        services.AddUdpMulticastWithConnectionManager(name);

        services.AddOptions<UdpMulticastOptions>(name)
            .PostConfigure<IOptionsMonitor<KnxConnectionOptions>>((opts, knxOptions) =>
            {
                var knx = knxOptions.Get(name).ToEffective();

                opts.MulticastAddress = knx.MulticastAddress;
                opts.Port = knx.Port;

                if (!string.IsNullOrWhiteSpace(knx.LocalInterface))
                    opts.LocalInterface = knx.LocalInterface;
                if (!string.IsNullOrWhiteSpace(knx.LocalIpAddress))
                    opts.LocalIpAddress = knx.LocalIpAddress;

                if (knx.TimeToLive.HasValue)
                    opts.TimeToLive = knx.TimeToLive.Value;
                if (knx.ReceiveBufferSize.HasValue)
                    opts.ReceiveBufferSize = knx.ReceiveBufferSize.Value;
                if (knx.SendBufferSize.HasValue)
                    opts.SendBufferSize = knx.SendBufferSize.Value;
                if (knx.ReuseAddress.HasValue)
                    opts.ReuseAddress = knx.ReuseAddress.Value;
                if (knx.MulticastLoopback.HasValue)
                    opts.MulticastLoopback = knx.MulticastLoopback.Value;
                if (knx.ReceiveTimeout.HasValue)
                    opts.ReceiveTimeout = knx.ReceiveTimeout.Value;
            });

        services.AddOptions<UdpConnectionManagerOptions>(name)
            .PostConfigure<IOptionsMonitor<KnxConnectionOptions>>((opts, knxOptions) =>
            {
                var knx = knxOptions.Get(name).ToEffective();

                if (knx.ReconnectInterval.HasValue)
                    opts.ReconnectInterval = knx.ReconnectInterval.Value;
                if (knx.SendRetryInterval.HasValue)
                    opts.SendRetryInterval = knx.SendRetryInterval.Value;
                if (knx.MaxSendAttempts.HasValue)
                    opts.MaxSendAttempts = knx.MaxSendAttempts.Value;
                if (knx.AutoConnect.HasValue)
                    opts.AutoConnect = knx.AutoConnect.Value;
            });

        // Infrastructure — all TryAdd (via AddKnxConfig / AddKnxCore), safe to call multiple times.
        services.AddKnxConfig();
        //services.AddKnxCore(); called by AddKnxConfig, which also registers the IKnxSystemConfiguration and IDptResolver that depend on core services
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
            var options      = Options.Create(sp.GetRequiredService<IOptionsMonitor<KnxConnectionOptions>>().Get(name));
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
                Options.Create(sp.GetRequiredService<IOptionsMonitor<KnxConnectionOptions>>().Get(name)),
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
