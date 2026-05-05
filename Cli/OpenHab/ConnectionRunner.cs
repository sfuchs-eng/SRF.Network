using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Cli.OpenHab;

public interface IConnectionRunner : IHostedService
{
    IEventBusClient GetOpenHabClient();
}

public sealed class ConnectionRunner : IConnectionRunner
{
    public ConnectionRunner(IEventBusClient ohclient, IRestApiClient restApi, IEventFactory eventFactory, ILogger<ConnectionRunner> logger)
    {
        logger.LogDebug("Initializing...");
        OHClient = ohclient;
        Logger = logger;
        EventFactory = eventFactory;
        RestApi = restApi;
    }

    private readonly IEventBusClient OHClient;
    private readonly IRestApiClient RestApi;
    private readonly ILogger<ConnectionRunner> Logger;
    public IEventFactory EventFactory { get; }

    double WeirdDouble { get; set; } = Math.PI * 1e12;

    public IEventBusClient GetOpenHabClient()
    {
        return OHClient;
    }

    private CancellationTokenSource? ConnectionCancellationSource = null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogInformation("Connecting...");
            ConnectionCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            //_ = Task.Run(() => OHClient.ConnectAsync(ConnectionCancellationSource.Token), cancellationToken);
            OHClient.EventReceived += OHClient_EventReceived;

            // set filter
            var wse = EventFactory.CreateFilterType(new EventType[] { EventType.ItemStateEvent, EventType.ItemStateChangedEvent });
            OHClient.EnqueueTransmit(wse);

            _ = Task.Run(() => SturdyNumberCounter(cancellationToken), cancellationToken);
        }
        catch ( OperationCanceledException ex)
        {
            Logger.LogInformation("Connection got canceled.");
            throw new OperationCanceledException("OpenHAB connection runner start got canceled.", ex, cancellationToken);
        }
        catch ( Exception ex )
        {
            Logger.LogError(ex, "Failed to connect to OpenHAB");
            Environment.Exit(1);
        }

        Logger.LogDebug("Fetching all items via REST API...");
        var items = await RestApi.GetItemsAsync(cancellationToken);
        Logger.LogInformation("Items list: {itemsList}", string.Join(", ", items.Select(i => i.Name)));

        await Task.CompletedTask;
    }

    private async Task SturdyNumberCounter(CancellationToken cancel)
    {
        long count = 0;
        while ( !cancel.IsCancellationRequested )
        {
            count++;
            await OHClient.SendAsync(EventFactory.Create<ItemEventTypeValue>(EventType.ItemCommandEvent).Set(count).ForItem("WeirdTestItemCounter"), cancel);
            await Task.Delay(1000);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await OHClient.CloseAsync(cancellationToken);
        Logger.LogInformation("Connection closed.");
    }

    public static readonly JsonSerializerOptions SpecialSerOpts = new()
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, /* we need the \" style encoding */
    };

    void OHClient_EventReceived(object? sender, EventReceivedEventArgs e)
    {
        /*
        if ( e.Received is ItemStateChangedEvent itemChanged )
        {
            Logger.LogInformation("Received OpenHAB event {evtType}: {IEvent}", itemChanged.GetType().Name, itemChanged.ToString());
        }
        else
            Logger.LogInformation("Received OpenHAB event {evtType}: {IEvent}", e.Received.GetType().Name, e.Received.ToString());
            */               

        if (e.IsItem("TestSwitch"))
        {
            Logger.LogInformation("TestSwitch event, sending item command to TestNumber, value {testValue}", WeirdDouble);
            OHClient.EnqueueTransmit(EventFactory.Create<ItemEventTypeValue>(EventType.ItemCommandEvent).Set(WeirdDouble).ForItem("TestNumber"));
            WeirdDouble /= 2.0;
        }
        else if (e.Received is ItemStateChangedEvent itemChangedEvt && itemChangedEvt.ItemName.Equals("TestSwitch"))
        {
            Logger.LogWarning("TestSwitch item event discovery failed.");
        }
        else if (e.Received is ItemEventTypeValue itemEvt && itemEvt.ItemName.Equals("TestSwitch"))
        {
            Logger.LogWarning("TestSwitch item event discovery failed.");
        }
    }

    void SpecialCommandTransmit()
    {
        var cmd = new SpecialItemCommand()
        {
            Type = EventType.ItemCommandEvent,
            Topic = "openhab/items/TestNumber/command",
            Payload = JsonSerializer.Serialize(new TypeValuePayload()
            {
                Type = "Decimal",
                Value = WeirdDouble.ToString()
            }, typeof(TypeValuePayload), SpecialSerOpts),
            ID = "-1"
        };

        var msg = JsonSerializer.Serialize(cmd, cmd.GetType(), SpecialSerOpts);
        OHClient.SendAsync(msg, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).Wait();
        Logger.LogDebug("TestNumber transmitted with {special}, msg: '{packet}'", nameof(SpecialItemCommand), msg);
    }
}
