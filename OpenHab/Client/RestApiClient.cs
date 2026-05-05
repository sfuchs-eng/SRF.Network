using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SRF.Network.OpenHab.Items;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace SRF.Network.OpenHab.Client
{
    public class RestApiClient : IRestApiClient
    {
        static readonly string ApiItems = "items?recursive=false";
        private readonly IHttpClientFactory httpClientFactory;

        HttpClient RestClient { get; }
        ILogger<RestApiClient> Logger { get; }

        JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions()
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, /* we may need the \" style encoding */
        };

        public RestApiClient(IHttpClientFactory httpClientFactory, IOptions<EventBusClientOptions> options, ILogger<RestApiClient> logger)
        {
            this.httpClientFactory = httpClientFactory;
            Logger = logger;
            RestClient = httpClientFactory.CreateClient();
            RestClient.BaseAddress = options.Value.RestApi;
            RestClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        }

        public async Task<Item[]> GetItemsAsync(CancellationToken cancel)
        {
            try
            {
#pragma warning disable CS1701 // Assuming assembly reference matches identity
                return await RestClient.GetFromJsonAsync<Item[]>(ApiItems, JsonOptions, cancel)
                    ?? throw new ProtocolException("Failed to obtain Items list.");
#pragma warning restore CS1701 // Assuming assembly reference matches identity
            }
            catch (OperationCanceledException oca)
            {
                throw new OperationCanceledException($"{nameof(GetItemsAsync)} cancelled.", oca, cancel);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "OpenHAB API request to '{baseUri}{apiUri}' failed.", RestClient.BaseAddress, ApiItems);
                throw new ProtocolException($"OpenHAB API request to '{RestClient.BaseAddress}{ApiItems}' failed.", ex);
            }
        }
    }
}
