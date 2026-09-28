using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Network.Cli;
using SRF.Network.OpenHab;
using SRF.Network.OpenHab.Client;
using SRF.Network.OpenHab.EventBus;
using SRF.Network.OpenHab.EventBus.Events;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class EventBusClientTests
{
    private sealed class TestCommand : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestableEventBusClient(IOptions<EventBusClientOptions> options, IEventFactory factory)
        : EventBusClient(options, factory, NullLogger<EventBusClient>.Instance)
    {
        public Uri ExposedRequestUri => RequestURI;
    }

    private static TestableEventBusClient CreateTestable(string webSocket, string accessToken = "mytoken")
    {
        var options = Options.Create(new EventBusClientOptions
        {
            WebSocket = webSocket,
            AccessToken = accessToken,
        });
        return new TestableEventBusClient(options, Substitute.For<IEventFactory>());
    }

    private static EventBusClient CreateClient(bool enable = true)
    {
        var options = Options.Create(new EventBusClientOptions
        {
            Enable = enable,
            WebSocket = "ws://localhost:8080/ws",
            AccessToken = "token",
        });

        var factory = Substitute.For<IEventFactory>();
        return new EventBusClient(options, factory, NullLogger<EventBusClient>.Instance);
    }

    [Test]
    public void IsConnected_Initially_False()
    {
        var client = CreateClient();

        Assert.That(client.IsConnected, Is.False);
    }

    [Test]
    public void IsActive_Initially_False()
    {
        var client = CreateClient();

        Assert.That(client.IsActive, Is.False);
    }

    [Test]
    public void ConnectAsync_WhenDisabled_CompletesOnCancellation()
    {
        var client = CreateClient(enable: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await client.ConnectAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void EventFactory_Create_ItemStateUpdatedEvent_ParsesPayload()
    {
        var factory = new EventFactory(NullLogger<EventFactory>.Instance);
        using var document = JsonDocument.Parse("""
        {
          "type": "ItemStateUpdatedEvent",
          "topic": "openhab/items/WP_TKompressorSaugTemp/stateupdated",
          "payload": "{\"type\":\"Quantity\",\"value\":\"23 °C\",\"lastStateUpdate\":\"2026-09-28T08:50:24.821760907+02:00[Europe/Zurich]\"}",
          "source": "org.openhab.core.thing$mqtt:topic:wasserGemuese:WifiRSSI"
        }
        """);

        var evt = factory.Create(document);

        Assert.That(evt, Is.TypeOf<ItemStateUpdatedEvent>());
        Assert.That(evt.Type, Is.EqualTo(EventType.ItemStateUpdatedEvent));
        Assert.That(((ItemEvent)evt).ItemName, Is.EqualTo("WP_TKompressorSaugTemp"));
    }

    [Test]
    public void EventBusClientOptions_DefaultFilterSource_IsDisabled()
    {
        var options = new EventBusClientOptions();

        Assert.That(options.FilterSource, Is.False);
    }

    [Test]
    public void EventBusClient_DefaultItemEventFilters_OnlyIncludeItemRelatedTypes()
    {
        var filters = EventBusClient.GetDefaultTypeFilters();

        Assert.That(filters, Does.Contain(EventType.ItemStateEvent));
        Assert.That(filters, Does.Contain(EventType.ItemStateUpdatedEvent));
        Assert.That(filters, Does.Contain(EventType.ItemStateChangedEvent));
        Assert.That(filters, Does.Not.Contain(EventType.ThingStatusInfoEvent));
        Assert.That(filters, Does.Not.Contain(EventType.WebSocketEvent));
    }

    [Test]
    public void HostLauncher_AppSettingsPath_IsResolvedFromExecutableDirectory()
    {
        var appSettingsPath = HostLauncher<TestCommand>.GetAppSettingsFilePath();

        Assert.That(Path.GetFileName(appSettingsPath), Is.EqualTo("appsettings.json"));
        Assert.That(Path.IsPathRooted(appSettingsPath), Is.True);
        Assert.That(appSettingsPath, Does.StartWith(AppContext.BaseDirectory));
    }

    [Test]
    public async Task WebSocketRecreator_DoesNotBlockSynchronouslyBeforeFirstAwait()
    {
        var client = CreateClient();
        var method = typeof(EventBusClient).GetMethod("WebSocketRecreator", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Expected the reconnect worker to exist.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var invokeTask = Task.Run(() =>
        {
            var result = method!.Invoke(client, [cts.Token]);
            Assert.That(result, Is.Not.Null);
            return (Task)result!;
        });

        await Task.Delay(200);
        Assert.That(invokeTask.IsCompleted, Is.False,
            "The reconnect worker must return a task without blocking the caller before its first await.");

        await invokeTask;
    }

    [Test]
    public void BuildRequestUri_BareUrl_AppendsAccessToken()
    {
        var client = CreateTestable("ws://localhost:8080/ws/events", "mytoken");

        var uri = client.ExposedRequestUri;

        Assert.That(uri.Query, Does.Contain("accessToken=mytoken"));
    }

    [Test]
    public void BuildRequestUri_TokenAlreadyInQueryString_DoesNotDuplicateToken()
    {
        var client = CreateTestable("ws://localhost:8080/ws/events?accessToken=existing", "mytoken");

        var uri = client.ExposedRequestUri;
        var tokenCount = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.StartsWith("accessToken=", StringComparison.OrdinalIgnoreCase));

        Assert.That(tokenCount, Is.EqualTo(1), $"Expected exactly one accessToken parameter, got: {uri}");
    }

    [Test]
    public void BuildRequestUri_OtherQueryParam_PreservesParamAndAppendsToken()
    {
        var client = CreateTestable("ws://localhost:8080/ws/events?other=val", "mytoken");

        var uri = client.ExposedRequestUri;

        Assert.That(uri.Query, Does.Contain("other=val"));
        Assert.That(uri.Query, Does.Contain("accessToken=mytoken"));
    }

    [Test]
    public void BuildRequestUri_PathIsSlashWs_NormalizesToWsEvents()
    {
        var client = CreateTestable("ws://localhost:8080/ws", "tok");

        var uri = client.ExposedRequestUri;

        Assert.That(uri.AbsolutePath, Is.EqualTo("/ws/events"));
    }

    [Test]
    public void BuildRequestUri_RootPath_NormalizesToWsEvents()
    {
        var client = CreateTestable("ws://localhost:8080/", "tok");

        var uri = client.ExposedRequestUri;

        Assert.That(uri.AbsolutePath, Is.EqualTo("/ws/events"));
    }
}
