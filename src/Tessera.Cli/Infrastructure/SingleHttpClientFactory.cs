namespace Tessera.Cli.Infrastructure;

public sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
