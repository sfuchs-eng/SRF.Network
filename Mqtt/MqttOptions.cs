using System.Reflection;

namespace SRF.Network.Mqtt;

/// <summary>
/// Options for an MQTT connection via <see cref="IMqttBrokerConnection"/> or the <see cref="MqttBrokerConnection"/> implementation of it
/// in an <code>appsettings.json</code> file or other configuration source.
/// </summary>
public class MqttOptions
{
    public static readonly string DefaultConfigSectionName = "Mqtt";

    public bool DisableConnection { get; set; } = false;

    public string Name { get; set; } = "default";
    public string ClientID { get; set; } = $"{Assembly.GetEntryAssembly()?.GetName()?.Name ?? Guid.NewGuid().ToString()}-{Environment.MachineName}";
    public string Host { get; set; } = "localhost";
    public string User { get; set; } = string.Empty;
    public string Pass { get; set; } = string.Empty;
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Seconds, keep alife time interval
    /// </summary>
    public double KeepAlifeTime { get; set; } = 60;

    /// <summary>
    /// Every how many seconds shall the broker be pinged to check the connection and reconnect if not connected?
    /// </summary>
    public double PingInterval { get; internal set; } = 10;

    /// <summary>
    /// If client is disconnected, retry publishing after how many seconds?
    /// </summary>
    public int PublishRetryInterval { get; internal set; } = 12;

    /// <summary>
    /// How many seconds to wait for additional subscriptions being enqueued prior subscribing with the broker?
    /// </summary>
    public double SubscriptionDelay { get; internal set; } = 1;
}
