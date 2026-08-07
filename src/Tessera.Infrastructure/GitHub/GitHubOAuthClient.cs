using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Tessera.Infrastructure.GitHub;

public sealed class GitHubOAuthOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "http://localhost:5080/api/auth/callback";
    public string WebUrl { get; set; } = "http://localhost:3000";
    public string AuthorizeUrl { get; set; } = "https://github.com/login/oauth/authorize";
    public string TokenUrl { get; set; } = "https://github.com/login/oauth/access_token";
}

public sealed record GitHubOAuthUser(string Login, string Name, string AvatarUrl);

public interface IGitHubOAuthClient
{
    string BuildAuthorizeUrl(string state);
    Task<string> ExchangeCodeAsync(string code, CancellationToken ct = default);
    Task<GitHubOAuthUser> GetUserAsync(string accessToken, CancellationToken ct = default);
    Task<IReadOnlyList<long>> GetUserInstallationsAsync(string accessToken, CancellationToken ct = default);
}

public sealed class GitHubOAuthClient : IGitHubOAuthClient
{
    private readonly HttpClient _http;
    private readonly GitHubOAuthOptions _options;

    public GitHubOAuthClient(IHttpClientFactory factory, GitHubOAuthOptions options)
    {
        _http = factory.CreateClient(nameof(GitHubOAuthClient));
        _http.BaseAddress = new Uri("https://api.github.com/");
        _options = options;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("tessera");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public string BuildAuthorizeUrl(string state)
    {
        var redirect = Uri.EscapeDataString(_options.CallbackUrl);
        var scope = Uri.EscapeDataString("read:user");
        return $"{_options.AuthorizeUrl}?client_id={_options.ClientId}&redirect_uri={redirect}&scope={scope}&state={state}";
    }

    public async Task<string> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _options.CallbackUrl
            })
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(ct);
        if (!string.IsNullOrEmpty(payload?.Error))
        {
            throw new InvalidOperationException(
                $"GitHub OAuth failed: {payload.Error}{(string.IsNullOrEmpty(payload.ErrorDescription) ? "" : $" ({payload.ErrorDescription})")}");
        }
        return payload?.AccessToken ?? throw new InvalidOperationException("GitHub OAuth returned no access token.");
    }

    public async Task<GitHubOAuthUser> GetUserAsync(string accessToken, CancellationToken ct = default)
    {
        var user = await GetAsync<GitHubUserResponse>("user", accessToken, ct);
        return new GitHubOAuthUser(user.Login, user.Name ?? user.Login, user.AvatarUrl ?? "");
    }

    public async Task<IReadOnlyList<long>> GetUserInstallationsAsync(string accessToken, CancellationToken ct = default)
    {
        var payload = await GetAsync<InstallationsResponse>("user/installations", accessToken, ct);
        return payload.Installations
            .Where(i => i.Id > 0)
            .Select(i => i.Id)
            .ToList();
    }

    private async Task<T> GetAsync<T>(string path, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidOperationException($"GitHub returned no payload for {path}.");
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private sealed class GitHubUserResponse
    {
        public string Login { get; set; } = "";
        public string? Name { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }

    private sealed class InstallationsResponse
    {
        public List<InstallationIdItem> Installations { get; set; } = new();
    }

    private sealed class InstallationIdItem
    {
        public long Id { get; set; }
    }
}
