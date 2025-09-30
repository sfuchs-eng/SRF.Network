using MQTTnet.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Mqtt;

public class MqttLoggingProxy : IMqttNetLogger
{
    private ILogger _logger;

    private readonly Dictionary<MqttNetLogLevel, LogLevel> LogLevelMap = new Dictionary<MqttNetLogLevel, LogLevel>();
    private readonly ILoggerFactory loggerFactory;

    public event EventHandler<MqttNetLogMessagePublishedEventArgs>? LogMessagePublished;

    public MqttLoggingProxy(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(nameof(MQTTnet));

        LogLevelMap.Add(MqttNetLogLevel.Error, LogLevel.Error);
        LogLevelMap.Add(MqttNetLogLevel.Warning, LogLevel.Warning);
        LogLevelMap.Add(MqttNetLogLevel.Info, LogLevel.Information);
        LogLevelMap.Add(MqttNetLogLevel.Verbose, LogLevel.Trace);
        this.loggerFactory = loggerFactory;
    }

    public bool IsEnabled => true;

    public void Publish(MqttNetLogLevel logLevel, string source, string message, object[] parameters, Exception exception)
    {
        if (exception != null)
            _logger.Log(LogLevelMap[logLevel], new EventId(0, source), exception, message, parameters);
        else
            _logger.Log(LogLevelMap[logLevel], new EventId(0, source), message, parameters);
    }
}
