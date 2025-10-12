using System;
using Knx.Falcon.Logging;
using KNI = Knx.Falcon.Sdk;
using Microsoft.Extensions.Logging;
using Knx.Falcon.Sdk;

namespace SRF.Network.Knx.FalconSupport;

public class MSLoggingFalconLogger(ILogger netLogger) : IFalconLogger
{
    private readonly ILogger netLogger = netLogger;

    public bool IsDebugEnabled => netLogger.IsEnabled(LogLevel.Debug) || netLogger.IsEnabled(LogLevel.Trace);

    public bool IsErrorEnabled => netLogger.IsEnabled(LogLevel.Error) || netLogger.IsEnabled(LogLevel.Critical);

    public bool IsInfoEnabled => netLogger.IsEnabled(LogLevel.Information);

    public bool IsWarnEnabled => netLogger.IsEnabled(LogLevel.Warning);

    public void Debug(object message)
    {
        netLogger.Log(LogLevel.Debug, "{msg}", message.ToString());
    }

    public void Debug(object message, Exception exception)
    {
        netLogger.Log(LogLevel.Debug, exception, "{msg}", message.ToString());
    }

    public void DebugFormat(string format, params object[] args)
    {
        netLogger.Log(LogLevel.Debug, format, args);
    }

    public void Error(object message)
    {
        netLogger.Log(LogLevel.Error, "{msg}", message.ToString());
    }

    public void Error(object message, Exception exception)
    {
        netLogger.Log(LogLevel.Error, exception, "{msg}", message.ToString());
    }

    public void ErrorFormat(string format, params object[] args)
    {
        netLogger.Log(LogLevel.Error, format, args);
    }

    public void Info(object message)
    {
        netLogger.Log(LogLevel.Information, "{msg}", message.ToString());
    }

    public void Info(object message, Exception exception)
    {
        netLogger.Log(LogLevel.Information, exception, "{msg}", message.ToString());
    }

    public void InfoFormat(string format, params object[] args)
    {
        netLogger.Log(LogLevel.Information, format, args);
    }

    public void Warn(object message)
    {
        netLogger.Log(LogLevel.Warning, "{msg}", message.ToString());
    }

    public void Warn(object message, Exception exception)
    {
        netLogger.Log(LogLevel.Warning, exception, "{msg}", message.ToString());
    }

    public void WarnFormat(string format, params object[] args)
    {
        netLogger.Log(LogLevel.Warning, format, args);
    }
}
