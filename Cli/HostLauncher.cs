using DotMake.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SRF.Network.Cli;

public class HostLauncher<TCommand>() where TCommand : BackgroundService
{
    protected virtual void AddConfiguration(IConfigurationBuilder configurationBuilder, CliContext cliContext)
    {
        configurationBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        configurationBuilder.AddCommandLine([.. cliContext.Result.ParseResult.UnmatchedTokens]);
    }

    protected virtual void AddLogging(ILoggingBuilder loggingBuilder, CliContext cliContext)
    {
        loggingBuilder.AddConsole();
    }

    protected virtual void AddServices(IServiceCollection services, CliContext cliContext)
    {
        services.AddSingleton(this.GetType(), this);
        services.AddSingleton<CliContext>(cliContext);
        services.AddHostedService<TCommand>();
    }

    public virtual async Task<int> RunAsync(CliContext cliContext)
    {
        var hostBuilder = Host.CreateApplicationBuilder([.. cliContext.Result.ParseResult.UnmatchedTokens]);

        AddConfiguration(hostBuilder.Configuration, cliContext);
        AddLogging(hostBuilder.Logging, cliContext);
        AddServices(hostBuilder.Services, cliContext);

        using var host = hostBuilder.Build();

        await host.RunAsync();

        return 0;
    }
}
