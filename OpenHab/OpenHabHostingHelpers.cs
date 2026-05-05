using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.OpenHab
{
    public static class OpenHabHostingHelpers
    {
        /// <summary>
        /// Adds all required services and configures options for <see cref="EventBusClient"/> using <see cref="EventBus.EventFactory"/>
        /// and installs <see cref="OpenHabConnector"/> as <see cref="IHostedService"/> to run it all.
        /// Ensure to call <see cref="IHostBuilder.ConfigureAppConfiguration(System.Action{HostBuilderContext, Microsoft.Extensions.Configuration.IConfigurationBuilder})"/> on beforehand.
        /// The <see cref="EventBus.EventFactory"/> can be retrieved as singleton service <see cref="IEventFactory"/>.
        /// The <see cref="EventBusClient"/> can be retrieved as singleton sevice <see cref="IEventBusClient"/>.
        /// </summary>
        public static IHostBuilder AddOpenHabConnector(this IHostBuilder hostBuilder, string configSectionName = "OpenHAB")
        {
            hostBuilder.ConfigureServices((ctx, s) =>
            {
                s.AddOpenHabConnector(configSectionName);
            });
            return hostBuilder;
        }

        public static IServiceCollection AddOpenHabConnector(this IServiceCollection services, string configSectionName = "OpenHAB")
        {
            services.AddOptions();
            services.AddOptions<EventBusClientOptions>().BindConfiguration(configSectionName);
            services.AddHttpClient();
            services.AddSingleton<IEventFactory, EventBus.EventFactory>();
            services.AddSingleton<IRestApiClient, RestApiClient>();
            services.AddSingleton<IEventBusClient, EventBusClient>();
            services.AddHostedService<OpenHabConnector>();
            return services;
        }
    }
}
