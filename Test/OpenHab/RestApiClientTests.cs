using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SRF.Network.OpenHab.Client;

namespace SRF.Network.Test.OpenHab;

[TestFixture]
public class RestApiClientTests
{
    private static EventBusClientOptions CreateOptions() => new()
    {
        RestApi = new Uri("http://localhost:8080/rest/"),
        AccessToken = "token",
    };

    [Test]
    public async Task GetItemsAsync_ReturnsDeserializedItems()
    {
        var handler = new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [{"name":"Light1","state":"ON","editable":true,"type":"Switch","label":"Light","category":"","tags":[],"groupNames":[]}]
                    """, Encoding.UTF8, "application/json"),
            });
        var client = CreateClient(handler);

        var items = await client.GetItemsAsync(CancellationToken.None);

        Assert.That(items, Has.Length.EqualTo(1));
        Assert.That(items[0].Name, Is.EqualTo("Light1"));
        Assert.That(items[0].State, Is.EqualTo("ON"));
    }

    [Test]
    public void GetItemsAsync_OnHttpFailure_ThrowsProtocolException()
    {
        var handler = new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        Assert.That(async () => await client.GetItemsAsync(CancellationToken.None),
            Throws.TypeOf<ProtocolException>());
    }

    [Test]
    public void GetItemsAsync_OnCancellation_ThrowsOperationCanceledException()
    {
        var handler = new TestHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await client.GetItemsAsync(cts.Token),
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task SetItemStateAsync_SendsPutRequestWithCorrectPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        await client.SetItemStateAsync("KitchenLight", "ON", CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(captured.RequestUri!.ToString(), Is.EqualTo("http://localhost:8080/rest/items/KitchenLight/state"));
    }

    [Test]
    public async Task SetItemStateAsync_EncodesSpecialCharsInItemName()
    {
        HttpRequestMessage? captured = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        await client.SetItemStateAsync("Room Light/1", "OFF", CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.RequestUri!.ToString(),
            Is.EqualTo("http://localhost:8080/rest/items/Room Light%2F1/state"));
    }

    [Test]
    public void SetItemStateAsync_OnHttpFailure_ThrowsProtocolException()
    {
        var handler = new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        Assert.That(async () => await client.SetItemStateAsync("X", "Y", CancellationToken.None),
            Throws.TypeOf<ProtocolException>());
    }

    [Test]
    public void SetItemStateAsync_EmptyItemName_ThrowsArgumentException()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        Assert.That(async () => await client.SetItemStateAsync("", "ON", CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    private static RestApiClient CreateClient(TestHttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient().Returns(new HttpClient(handler));
        return new RestApiClient(
            factory,
            Options.Create(CreateOptions()),
            NullLogger<RestApiClient>.Instance);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? _responderWithCt;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = _ => throw new InvalidOperationException("Use cancellation-token responder path.");
            _responderWithCt = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responderWithCt is null
                ? _responder(request)
                : _responderWithCt(request, cancellationToken);
            return Task.FromResult(response);
        }
    }
}
