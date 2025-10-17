using Knx.Falcon.Logging;
using Microsoft.Extensions.DependencyInjection;
using SRF.Network.Knx.Domain;
using SRF.Network.Knx.FalconSupport;

namespace SRF.Network.Knx;

public static class ExtensionsHosting
{
    public static IServiceCollection AddKnx<TConnector>(this IServiceCollection services)
        where TConnector : class, IKnxConnection
    {
        services.AddOptions<KnxConfiguration>().BindConfiguration(KnxConfiguration.SectionName);
        services.AddSingleton((s) =>
        {
            var dcf = s.GetRequiredService<IDomainConfigurationFactory>();
            return dcf.Load();
        });

        services.AddSingleton<IFalconLoggerFactory, MSLoggingFalconLoggerFactory>();
        services.AddSingleton<FalconInitializer>();
        services.AddSingleton<IDomainConfigurationFactory, DomainConfigurationFactory>();

        services.AddSingleton<IKnxConnection, TConnector>();
        return services;
    }
}
