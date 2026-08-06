using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tessera.Integration.Tests;

public sealed class AuthMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MigrateOnStartup", "false");
            builder.UseSetting("Database:InMemory", "true");
            builder.UseSetting("Database:Name", Guid.NewGuid().ToString());
            builder.UseSetting("Dashboard:ApiKey", "test-key");
        });
    }

    [Fact]
    public async Task Api_data_without_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/repositories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_data_with_valid_bearer_token_is_allowed()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/repositories");
        request.Headers.Authorization = new("Bearer", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_data_with_wrong_token_is_rejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/repositories");
        request.Headers.Authorization = new("Bearer", "wrong-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Github_webhook_path_is_exempt_from_dashboard_auth()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/webhook");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_config_is_public_and_reports_oauth_disabled()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(
            await response.Content.ReadAsStringAsync());
        Assert.False(payload!["githubEnabled"]);
    }
}
