using Knx.Falcon.Logging;
using Microsoft.Extensions.DependencyInjection;
using SRF.Network.Knx.FalconSupport;

namespace SRF.Network.Knx;

public static class ExtensionsHosting
{
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddSingleton<IFalconLoggerFactory, MSLoggingFalconLoggerFactory>();
        services.AddSingleton<FalconInitializer>();

        services.AddOptions<KnxConfiguration>(KnxConfiguration.SectionName);

        services.AddSingleton<IKnxConnection, TConnector>();
        return services;
    }
}
