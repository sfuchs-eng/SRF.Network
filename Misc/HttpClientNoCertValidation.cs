namespace SRF.Network.Misc;

public class HttpClientNoCertValidation : HttpClient
{
    public HttpClientNoCertValidation() : base(HttpClientHandlerNoCertValidation)
    {
    }

    private static readonly HttpClientHandler HttpClientHandlerNoCertValidation = new()
    {
        ServerCertificateCustomValidationCallback = (msg, cert, chain, sslPolicyErrors) =>
        {
            return true;
        }
    };
}