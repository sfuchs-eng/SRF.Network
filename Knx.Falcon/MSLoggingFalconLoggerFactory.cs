using Knx.Falcon.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Knx.FalconSupport;

public class MSLoggingFalconLoggerFactory : IFalconLoggerFactory
{
    private readonly ILoggerFactory loggerFactory;

    public MSLoggingFalconLoggerFactory(IServiceProvider serviceProvider)
    {
        this.loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
    }

    public IFalconLogger GetLogger(string name)
    {
        var x = loggerFactory.CreateLogger(name);
        return new MSLoggingFalconLogger(x);
    }
}
