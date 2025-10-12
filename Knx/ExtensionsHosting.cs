using Microsoft.Extensions.DependencyInjection;

namespace SRF.Network.Knx;

public static class ExtensionsHosting
{
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddOptions<KnxConfiguration>(KnxConfiguration.SectionName);
        services.AddSingleton<IKnxConnection, TConnector>();
        return services;
    }
}
