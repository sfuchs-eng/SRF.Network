using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
}
