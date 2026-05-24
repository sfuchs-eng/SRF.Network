using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SRF.Network.Mqtt.Hosting;

public static class MqttHostingExtensions
{
    public static IServiceCollection AddMqtt(this IServiceCollection s, string? configSection)
    {
        s.AddTransient<MQTTnet.Diagnostics.Logger.IMqttNetLogger, MqttLoggingProxy>();
        s.AddOptions<MqttOptions>().BindConfiguration(configSection ?? MqttOptions.DefaultConfigSectionName);
        s.AddSingleton<IMqttBrokerConnection, MqttBrokerConnection>();
        s.AddHostedService<ConnectionManager>();
        return s;
    }

    public static IHostBuilder AddMqtt(this IHostBuilder hostBuilder, string? configSection)
    {
        return hostBuilder.ConfigureServices((ctx, s) =>
        {
            s.AddMqtt(configSection);
        });
    }

    public static IHostApplicationBuilder AddMqtt(this IHostApplicationBuilder hostApplicationBuilder, string? configSection)
    {
        hostApplicationBuilder.Services.AddMqtt(configSection);
        return hostApplicationBuilder;
    }

    /// <summary>
    /// Registers a keyed MQTT broker connection using a concrete configuration section.
    /// </summary>
    /// <remarks>
    /// The returned keyed service is <see cref="IMqttBrokerConnection"/> and must be resolved via
    /// <c>GetRequiredKeyedService&lt;IMqttBrokerConnection&gt;(name)</c>.
    /// Lifecycle start/stop is intentionally controlled by the consuming integration.
    /// </remarks>
    public static IServiceCollection AddMqtt(this IServiceCollection services, string name, IConfigurationSection connectionSection)
    {
        services.AddTransient<MQTTnet.Diagnostics.Logger.IMqttNetLogger, MqttLoggingProxy>();
        services.AddOptions<MqttOptions>(name).Bind(connectionSection);

        services.AddKeyedSingleton<IMqttBrokerConnection>(name, (sp, _) =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<MqttOptions>>();
            var namedOptions = Options.Create(monitor.Get(name));
            return ActivatorUtilities.CreateInstance<MqttBrokerConnection>(sp, namedOptions);
        });

        return services;
    }
}
