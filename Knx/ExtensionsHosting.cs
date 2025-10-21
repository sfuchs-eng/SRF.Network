using Knx.Falcon.Logging;
using Microsoft.Extensions.DependencyInjection;
using SRF.Knx.Config;
using SRF.Network.Knx.FalconSupport;
using SRF.Network.Misc;

namespace SRF.Network.Knx;

public static class ExtensionsHosting
{
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddKnxConfig();

        services.AddSingleton<IFalconLoggerFactory, MSLoggingFalconLoggerFactory>();
        services.AddSingleton<FalconInitializer>();

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IKnxConnection, TConnector>();
        return services;
    }
}
